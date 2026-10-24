using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TransfersCheck.Api.Auth;
using TransfersCheck.Api.Jobs;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Billing;

public static class BillingEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/billing", Price);
        group.MapGet("/referral", Referral);
        group.MapPost("/billing/checkout", Checkout);
        app.MapPost("/api/billing/webhook", Webhook);
        app.MapGet("/api/billing/return", Return);
    }

    private static async Task<IResult> Referral(
        HttpContext http,
        IReferralStore referrals,
        CancellationToken cancellationToken)
    {
        if (http.User.FindFirst("owner")?.Value != "1")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var account = (LoginAccount)http.Items["account"]!;
        var code = await referrals.GetOrCreateCodeAsync(account.TenantId, cancellationToken);
        return Results.Ok(new { label = "Mi código", code, cashbackBalance = (decimal?)null });
    }

    private static async Task<IResult> Price(
        HttpContext http,
        IMerchantStore store,
        IPlanCatalog plans,
        IConsultaCounter consultas,
        IOptions<CommercialOptions> commercial,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        var active = await plans.ListActiveAsync(cancellationToken);
        var current = merchant?.PlanCode is { } code
            ? active.FirstOrDefault(plan => string.Equals(plan.Code, code, StringComparison.OrdinalIgnoreCase))
            : null;
        var now = clock.GetUtcNow();
        var quotaLevel = QuotaLevel.Ok;
        var used = 0;
        if (merchant?.ConnectedAt is { } link)
        {
            var (periodStart, periodEnd) = WeeklyQueryQuota.Period(link, now);
            used = await consultas.CountAsync(account.TenantId, periodStart, periodEnd, cancellationToken);
            quotaLevel = WeeklyQueryQuota.Evaluate(used, commercial.Value.WeeklyQueryCap, commercial.Value.WeeklyQueryWarningGap);
        }
        return Results.Ok(new
        {
            accountLabel = "Email",
            email = account.Email,
            planLabel = "Plan",
            planCode = merchant?.PlanCode,
            planName = current?.DisplayName,
            accessUntil = merchant?.AccessUntil,
            fiveDayWarning = FiveDayNotice.ShouldWarn(merchant?.PlanCode, merchant?.AccessUntil, now),
            blocked = AccessBlock.IsBlocked(merchant?.AccessUntil, now),
            consultaWarn = quotaLevel is QuotaLevel.Warn,
            consultaWait = quotaLevel is QuotaLevel.Wait,
            consultasUsed = used,
            consultasCap = commercial.Value.WeeklyQueryCap,
            plans = active.Select(plan => new
            {
                code = plan.Code,
                displayName = plan.DisplayName,
                amount = plan.Amount,
                currency = plan.Currency,
                durationDays = plan.DurationDays,
                durationHours = plan.DurationHours,
                faceCopy = plan.FaceCopy,
                enableCopilotHelp = plan.EnableCopilotHelp
            })
        });
    }

    private sealed record CheckoutBody(string? ReferralCode);

    private static async Task<IResult> Checkout(
        CheckoutBody? body,
        HttpContext http,
        ICheckout checkout,
        IPlanCatalog plans,
        IReferralStore referrals,
        CancellationToken cancellationToken)
    {
        if (http.User.FindFirst("owner")?.Value != "1")
            return Results.Json(new { error = "owner_only" }, statusCode: StatusCodes.Status403Forbidden);

        var account = (LoginAccount)http.Items["account"]!;
        if (!string.IsNullOrWhiteSpace(body?.ReferralCode))
            await referrals.AttachCodeAsync(account.TenantId, body.ReferralCode, cancellationToken);

        var packMes = await plans.FindByCodeAsync(PlanCodes.PackMes, cancellationToken)
            ?? throw new InvalidOperationException("Pack Mes plan row is missing.");
        // Invited commerce always pays the full Pack Mes amount — referral is not a discount.
        var initPoint = await checkout.CreatePaymentLinkAsync(
            account.TenantId,
            packMes.Amount,
            packMes.Currency,
            "TransfersCheck",
            cancellationToken);
        return Results.Ok(new { initPoint, amount = packMes.Amount });
    }

    private static async Task<IResult> Webhook(
        HttpContext http,
        ICheckout checkout,
        IMerchantStore store,
        IEntitlement entitlement,
        IPlanCatalog plans,
        IReferralStore referrals,
        ReferralBonusJob job,
        IOptions<CheckoutOptions> checkoutOptions,
        IOptions<CommercialOptions> commercial,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var provided = http.Request.Headers["X-Webhook-Secret"].ToString();
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(checkoutOptions.Value.WebhookSecret));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        if (checkoutOptions.Value.WebhookSecret.Length == 0 || !CryptographicOperations.FixedTimeEquals(expected, actual))
            return Results.Unauthorized();

        string? paymentId = http.Request.Query["id"];
        if (http.Request.ContentLength is > 0)
        {
            using var doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id))
                paymentId = id.ToString();
        }

        if (string.IsNullOrWhiteSpace(paymentId))
            return Results.BadRequest(new { error = "payment_required" });
        if (await store.PaymentAlreadyAppliedAsync(paymentId, cancellationToken))
            return Results.Ok(new { applied = false });

        var reference = await checkout.ReadApprovedPaymentAsync(paymentId, cancellationToken);
        if (reference is null || !Guid.TryParse(reference, out var tenantId))
            return Results.Ok(new { applied = false });

        var merchant = await store.FindMerchantAsync(tenantId, cancellationToken);
        if (merchant is null)
            return Results.Ok(new { applied = false });

        var paidAt = clock.GetUtcNow();
        var packMes = await plans.FindByCodeAsync(PlanCodes.PackMes, cancellationToken);
        var days = packMes?.DurationDays ?? commercial.Value.PaidPeriodDays;
        merchant.AccessUntil = entitlement.Extend(merchant.AccessUntil, paidAt, days);
        merchant.PlanCode = PlanCodes.PackMes;
        await store.AddProcessedPaymentAsync(new ProcessedPayment
        {
            PaymentId = paymentId,
            TenantId = tenantId,
            AppliedAt = paidAt
        }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await referrals.QueueGrantFromPackMesPaymentAsync(tenantId, paidAt, cancellationToken);
        await job.Execute();
        return Results.Ok(new { applied = true });
    }

    private static IResult Return(IOptions<UiOptions> ui)
    {
        var origin = ui.Value.Origin.TrimEnd('/');
        return Results.Redirect(string.IsNullOrEmpty(origin) ? "/" : origin + "/");
    }
}
