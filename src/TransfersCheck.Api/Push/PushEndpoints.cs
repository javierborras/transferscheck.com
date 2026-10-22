using Microsoft.Extensions.Options;
using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Push;

public static class PushEndpoints
{
    public sealed record PushToken(string? Token);

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/push/config", Config);
        group.MapPost("/push/tokens", Register);
    }

    private static IResult Config(IOptions<FirebaseOptions> firebase)
        => Results.Ok(new
        {
            apiKey = firebase.Value.ApiKey,
            authDomain = firebase.Value.AuthDomain,
            messagingSenderId = firebase.Value.MessagingSenderId,
            appId = firebase.Value.AppId,
            vapidKey = firebase.Value.VapidKey,
            projectId = firebase.Value.ProjectId
        });

    private static async Task<IResult> Register(
        PushToken body,
        HttpContext http,
        IMerchantStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Token))
            return Results.BadRequest(new { error = "token_required" });

        var account = (LoginAccount)http.Items["account"]!;
        await store.AddPushDeviceAsync(new PushDevice
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            AccountId = account.Id,
            Token = body.Token.Trim()
        }, cancellationToken);

        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        if (merchant is not null)
        {
            var connection = await store.FindConnectionAsync(cancellationToken);
            merchant.ChecklistStep = ChecklistRules.Step(true, connection is not null, true);
            await store.SaveChangesAsync(cancellationToken);
        }

        return Results.NoContent();
    }
}
