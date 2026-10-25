using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using TransfersCheck.Infrastructure;
using TransfersCheck.Api.Auth;
using TransfersCheck.Api.Jobs;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Billing;

public static class BillingEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        app.MapGet("/api/plans", async (IPlanCatalog plans, CancellationToken cancellationToken) =>
            Results.Ok(new { plans = (await plans.ListActiveAsync(cancellationToken)).Select(PublicPlan) }));
        group.MapPost("/billing/initial-plan", InitialPlan);
        group.MapGet("/billing", Price);
        group.MapGet("/referral", Referral);
        group.MapPost("/billing/checkout", Checkout);
        app.MapPost("/api/billing/webhook", Webhook);
        app.MapGet("/api/billing/return", Return);
    }

    private static object PublicPlan(Plan plan) => new {
        code = plan.Code, displayName = plan.DisplayName, amount = plan.Amount, currency = plan.Currency,
        durationDays = plan.DurationDays, durationHours = plan.DurationHours, faceCopy = plan.FaceCopy,
        enableCopilotHelp = plan.EnableCopilotHelp
    };

    private sealed record InitialPlanBody(string? PlanCode);
    private static async Task<IResult> InitialPlan(InitialPlanBody body, HttpContext http, IMerchantStore store, IPlanCatalog plans, CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner) return Results.StatusCode(403);
        var plan = await plans.FindByCodeAsync(body.PlanCode ?? "", cancellationToken);
        if (plan is null || !plan.Active || plan.Code is not (PlanCodes.PackMes or PlanCodes.PackEvento)) return Results.BadRequest();
        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        if (merchant is null || merchant.ConnectedAt is not null || merchant.AccessUntil is not null) return Results.Conflict();
        merchant.PlanCode = plan.Code;
        await store.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
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
            trialEligible = PackMesTrial.Applies(merchant?.PlanCode) && merchant?.TrialEndsAt is null && merchant?.AccessUntil is null,
            alertPointCap = PackMesTrial.Applies(merchant?.PlanCode) ? 4 : 0,
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

    private sealed record CheckoutBody(string? ReferralCode, string? PlanCode);

    private static async Task<IResult> Checkout(
        CheckoutBody? body,
        HttpContext http,
        ICheckout checkout,
        IPlanCatalog plans,
        IReferralStore referrals,
        ITokenProtector protector,
        CancellationToken cancellationToken)
    {
        if (http.User.FindFirst("owner")?.Value != "1")
            return Results.Json(new { error = "owner_only" }, statusCode: StatusCodes.Status403Forbidden);

        var account = (LoginAccount)http.Items["account"]!;
        if (!string.IsNullOrWhiteSpace(body?.ReferralCode))
            await referrals.AttachCodeAsync(account.TenantId, body.ReferralCode, cancellationToken);

        var plan = await plans.FindByCodeAsync(body?.PlanCode ?? PlanCodes.PackMes, cancellationToken);
        if (plan is null || !plan.Active || plan.Code is not (PlanCodes.PackMes or PlanCodes.PackEvento)
            || plan.Amount <= 0 || (plan.DurationDays is not > 0 && plan.DurationHours is not > 0))
            return Results.BadRequest(new { error = "invalid_plan" });
        // Bind the purchased price and duration to this payment, even if the catalog changes later.
        var order = new CheckoutOrder(account.TenantId, plan.Code, plan.Amount, plan.Currency, plan.DurationDays, plan.DurationHours);
        var reference = protector.Protect($"checkout:{order.TenantId:N}|{order.PlanCode}|{order.Amount}|{order.Currency}|{order.DurationDays ?? 0}|{order.DurationHours ?? 0}");
        var initPoint = await checkout.CreatePaymentLinkAsync(reference, plan.Amount, plan.Currency, plan.DisplayName, cancellationToken);
        return Results.Ok(new { initPoint, amount = plan.Amount, planCode = plan.Code });
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
        ITokenProtector protector,
        TransfersCheckDb db,
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
        var payment = await checkout.ReadApprovedPaymentAsync(paymentId, cancellationToken);
        if (payment is null) return Results.Ok(new { applied = false });
        CheckoutOrder? order;
        if (Guid.TryParse(payment.Reference, out var legacyTenant))
        {
            // Preferences issued before plan-aware checkout were always Pack Mes.
            var legacyPlan = await plans.FindByCodeAsync(PlanCodes.PackMes, cancellationToken);
            order = new(legacyTenant, PlanCodes.PackMes, (int)payment.Amount, payment.Currency, legacyPlan?.DurationDays ?? commercial.Value.PaidPeriodDays, null);
        }
        else
        {
            try {
                var plain = protector.Unprotect(payment.Reference);
                var parts = plain.StartsWith("checkout:", StringComparison.Ordinal) ? plain[9..].Split('|') : [];
                order = parts.Length == 6 ? new CheckoutOrder(Guid.Parse(parts[0]), parts[1], int.Parse(parts[2]), parts[3], int.Parse(parts[4]), int.Parse(parts[5])) : null;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { order = null; }
            if (order is null || payment.Amount != order.Amount || payment.Currency != order.Currency)
                return Results.Ok(new { applied = false });
        }
        var tenantId = order.TenantId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var resource = $"TransfersCheck.Billing.{tenantId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Billing lock unavailable', 1;", cancellationToken);
        if (await store.PaymentAlreadyAppliedAsync(paymentId, cancellationToken))
            return Results.Ok(new { applied = false });

        var merchant = await store.FindMerchantAsync(tenantId, cancellationToken);
        if (merchant is null)
            return Results.Ok(new { applied = false });

        var paidAt = clock.GetUtcNow();
        var start = merchant.AccessUntil is { } until && until > paidAt ? until : paidAt;
        merchant.AccessUntil = order.DurationHours is > 0 ? start.AddHours(order.DurationHours.Value) : start.AddDays(order.DurationDays!.Value);
        merchant.PlanCode = order.PlanCode;
        await store.AddProcessedPaymentAsync(new ProcessedPayment
        {
            PaymentId = paymentId,
            TenantId = tenantId,
            AppliedAt = paidAt
        }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        if (order.PlanCode == PlanCodes.PackMes)
            await referrals.QueueGrantFromPackMesPaymentAsync(tenantId, paidAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (order.PlanCode == PlanCodes.PackMes) await job.Execute();
        return Results.Ok(new { applied = true });
    }

    private static IResult Return(IOptions<UiOptions> ui)
    {
        var origin = ui.Value.Origin.TrimEnd('/');
        return Results.Redirect(string.IsNullOrEmpty(origin) ? "/" : origin + "/");
    }
}
