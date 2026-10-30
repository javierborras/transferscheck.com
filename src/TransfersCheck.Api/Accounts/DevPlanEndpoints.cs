using TransfersCheck.Api.Auth;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Accounts;

/// <summary>
/// Development-only seam so browser tests can assign an active plan code without the owner microsite.
/// </summary>
public static class DevPlanEndpoints
{
    public sealed record Body(string? PlanCode);
    public sealed record AccessBody(DateTimeOffset? AccessUntil);
    public sealed record BillingOrderBody(int? Amount, string? PlanCode, bool? Paid);

    public static void Map(WebApplication app)
    {
        // Test-only seams are available to local Development and the isolated
        // integration-test host, never to a Production host.
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
            return;

        app.MapPut("/api/dev/plan-code", Set)
            .AddEndpointFilter<LiveSessionFilter>();
        app.MapPut("/api/dev/access-until", SetAccess)
            .AddEndpointFilter<LiveSessionFilter>();
        app.MapPost("/api/dev/billing-order", CreateBillingOrder)
            .AddEndpointFilter<LiveSessionFilter>();
        app.MapGet("/api/dev/email-code", PeekCode);
    }

    private static async Task<IResult> PeekCode(
        string email,
        IEmailCodeStore codes,
        CancellationToken cancellationToken)
    {
        var code = await codes.PeekPlainCodeAsync(email, cancellationToken);
        return code is null ? Results.NotFound() : Results.Ok(new { code });
    }

    private static async Task<IResult> Set(
        Body body,
        HttpContext http,
        IMerchantStore store,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        if (merchant is null)
            return Results.NotFound();

        merchant.PlanCode = body.PlanCode;
        await store.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SetAccess(
        AccessBody body,
        HttpContext http,
        IMerchantStore store,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        if (merchant is null)
            return Results.NotFound();

        merchant.AccessUntil = body.AccessUntil;
        await store.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateBillingOrder(
        BillingOrderBody body,
        HttpContext http,
        TransfersCheckDb db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var now = clock.GetUtcNow();
        var paid = body.Paid == true ? now : (DateTimeOffset?)null;
        var order = new BillingOrder
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            PlanCode = string.IsNullOrWhiteSpace(body.PlanCode) ? PlanCodes.PackMes : body.PlanCode.Trim(),
            Amount = body.Amount is > 0 ? body.Amount.Value : 1000,
            Currency = "ARS",
            CreatedAt = now,
            PaidAt = paid,
            ActivatedAt = paid
        };
        db.BillingOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { id = order.Id, tenantId = order.TenantId, amount = order.Amount });
    }
}
