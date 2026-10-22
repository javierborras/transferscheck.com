using System.IdentityModel.Tokens.Jwt;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Auth;

public sealed class LiveSessionFilter(IMerchantStore store, ITenantContext tenant, TimeProvider clock) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();

        var accountId = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var sessionId = Guid.Parse(user.FindFirst("sid")!.Value);
        tenant.TenantId = Guid.Parse(user.FindFirst("tid")!.Value);

        var account = await store.FindAccountAsync(accountId, context.HttpContext.RequestAborted);
        var session = await store.FindSessionAsync(sessionId, context.HttpContext.RequestAborted);
        var now = clock.GetUtcNow();
        if (account is null
            || account.Revoked
            || account.LiveSessionId != sessionId
            || session is null
            || session.ReplacedAt is not null
            || session.ExpiresAt <= now)
            return Results.Unauthorized();

        context.HttpContext.Items["account"] = account;
        return await next(context);
    }
}
