using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Accounts;

/// <summary>
/// Development-only seam so browser tests can assign an active plan code without the owner microsite.
/// </summary>
public static class DevPlanEndpoints
{
    public sealed record Body(string? PlanCode);
    public sealed record AccessBody(DateTimeOffset? AccessUntil);

    public static void Map(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return;

        app.MapPut("/api/dev/plan-code", Set)
            .AddEndpointFilter<LiveSessionFilter>();
        app.MapPut("/api/dev/access-until", SetAccess)
            .AddEndpointFilter<LiveSessionFilter>();
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
}
