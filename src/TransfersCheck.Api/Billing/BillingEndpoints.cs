using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Billing;

public static class BillingEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/billing", Price);
        group.MapPost("/billing/checkout", Checkout);
        app.MapPost("/api/billing/webhook", Webhook);
        app.MapGet("/api/billing/return", Return);
    }

    private static async Task<IResult> Price(
        HttpContext http,
        IMerchantStore store,
        IOptions<CommercialOptions> commercial,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        return Results.Ok(new
        {
            amount = commercial.Value.PriceAmount,
            currency = commercial.Value.Currency,
            paidPeriodDays = commercial.Value.PaidPeriodDays,
            accessUntil = merchant?.AccessUntil
        });
    }

    private static async Task<IResult> Checkout(
        HttpContext http,
        ICheckout checkout,
        IOptions<CommercialOptions> commercial,
        CancellationToken cancellationToken)
    {
        if (http.User.FindFirst("owner")?.Value != "1")
            return Results.Json(new { error = "owner_only" }, statusCode: StatusCodes.Status403Forbidden);

        var account = (LoginAccount)http.Items["account"]!;
        var initPoint = await checkout.CreatePreferenceAsync(
            account.TenantId,
            commercial.Value.PriceAmount,
            commercial.Value.Currency,
            "TransfersCheck",
            cancellationToken);
        return Results.Ok(new { initPoint });
    }

    private static async Task<IResult> Webhook(
        HttpContext http,
        ICheckout checkout,
        IMerchantStore store,
        IEntitlement entitlement,
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
        merchant.AccessUntil = entitlement.Extend(merchant.AccessUntil, paidAt, commercial.Value.PaidPeriodDays);
        await store.AddProcessedPaymentAsync(new ProcessedPayment
        {
            PaymentId = paymentId,
            TenantId = tenantId,
            AppliedAt = paidAt
        }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { applied = true });
    }

    private static IResult Return(IOptions<UiOptions> ui)
    {
        var origin = ui.Value.Origin.TrimEnd('/');
        return Results.Redirect(string.IsNullOrEmpty(origin) ? "/" : origin + "/");
    }
}
