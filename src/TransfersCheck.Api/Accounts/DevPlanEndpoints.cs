using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Accounts;

/// <summary>
/// Development-only seam so browser tests can set plan code until wave 9 owns the plan table.
/// </summary>
public static class DevPlanEndpoints
{
    public sealed record Body(string? PlanCode);

    public static void Map(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return;

        app.MapPut("/api/dev/plan-code", Set)
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
}
