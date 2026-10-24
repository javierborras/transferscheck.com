using Microsoft.Extensions.Options;
using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Connections;

public static class ConnectionEndpoints
{
    public const string Help = "1. Tenés que administrar esa cuenta de Mercado Pago. 2. Tocá Conectar. 3. Iniciá sesión y aceptá. 4. Volvés acá.";

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        group.MapPost("/connections/mercadopago", Begin);
        group.MapGet("/connections/mercadopago/callback", Callback);
        group.MapGet("/checklist", Checklist);
    }

    private static async Task<IResult> Begin(
        HttpContext http,
        IMerchantStore store,
        ITokenProtector protector,
        IOptions<MercadoPagoOptions> options,
        IMerchantAuthorizer authorizer,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var account = Account(http);
        var verifier = Pkce.Verifier();
        var state = Guid.NewGuid().ToString("N");
        var now = clock.GetUtcNow();
        await store.AddOAuthAttemptAsync(new OAuthAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            State = state,
            CodeVerifierProtected = protector.Protect(verifier),
            ExpiresAt = now.Add(options.Value.AttemptLifetime)
        }, cancellationToken);

        return Results.Ok(new { authorizationUrl = authorizer.BuildAuthorizationUrl(state, Pkce.Challenge(verifier)) });
    }

    private static async Task<IResult> Callback(
        string? code,
        string? state,
        HttpContext http,
        IMerchantStore store,
        ITokenProtector protector,
        IMerchantAuthorizer authorizer,
        IEntitlement entitlement,
        IOptions<UiOptions> ui,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Results.BadRequest(new { error = "missing_code" });

        var attempt = await store.FindOAuthAttemptAsync(state, cancellationToken);
        var now = clock.GetUtcNow();
        if (attempt is null || attempt.ExpiresAt <= now)
        {
            if (attempt is not null)
                await store.DeleteOAuthAttemptAsync(attempt, cancellationToken);
            return Results.BadRequest(new { error = "expired_state" });
        }

        var verifier = protector.Unprotect(attempt.CodeVerifierProtected);
        TokenSet tokens;
        try
        {
            tokens = await authorizer.ExchangeAsync(code, verifier, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Results.BadRequest(new { error = "exchange_failed" });
        }

        var account = Account(http);
        if (await store.ExternalAccountLinkedElsewhereAsync(tokens.ExternalAccountId, account.TenantId, cancellationToken))
            return Results.Conflict(new { error = "account_linked" });

        var existing = await store.FindConnectionAsync(cancellationToken);
        if (existing is null)
        {
            await store.AddConnectionAsync(new MerchantConnection
            {
                Id = Guid.NewGuid(),
                TenantId = account.TenantId,
                Source = "mercadopago",
                ExternalAccountId = tokens.ExternalAccountId,
                AccessTokenProtected = protector.Protect(tokens.AccessToken),
                RefreshTokenProtected = protector.Protect(tokens.RefreshToken),
                AccessTokenExpiresAt = tokens.ExpiresAt
            }, cancellationToken);
        }
        else
        {
            existing.ExternalAccountId = tokens.ExternalAccountId;
            existing.AccessTokenProtected = protector.Protect(tokens.AccessToken);
            existing.RefreshTokenProtected = protector.Protect(tokens.RefreshToken);
            existing.AccessTokenExpiresAt = tokens.ExpiresAt;
            await store.SaveChangesAsync(cancellationToken);
        }

        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        if (merchant is not null && merchant.TrialEndsAt is null)
        {
            // Wave 9 replaces this with the 21-day link rule.
            const int trialDays = 14;
            var trialEnd = entitlement.StartTrial(now, trialDays);
            merchant.ConnectedAt = now;
            merchant.TrialEndsAt = trialEnd;
            merchant.AccessUntil = trialEnd;
        }

        if (merchant is not null)
        {
            var push = await store.HasPushDeviceAsync(cancellationToken);
            merchant.ChecklistStep = ChecklistRules.Step(true, true, push);
            await store.SaveChangesAsync(cancellationToken);
        }

        await store.DeleteOAuthAttemptAsync(attempt, cancellationToken);
        var origin = ui.Value.Origin.TrimEnd('/');
        return Results.Redirect(string.IsNullOrEmpty(origin) ? "/" : origin + "/");
    }

    private static async Task<IResult> Checklist(
        HttpContext http,
        IMerchantStore store,
        IOptions<OnboardingOptions> onboarding,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = Account(http);
        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        var connection = await store.FindConnectionAsync(cancellationToken);
        var push = await store.HasPushDeviceAsync(cancellationToken);
        var step = ChecklistRules.Step(true, connection is not null, push);
        if (merchant is not null && merchant.ChecklistStep != step)
        {
            merchant.ChecklistStep = step;
            await store.SaveChangesAsync(cancellationToken);
        }

        var now = clock.GetUtcNow();
        return Results.Ok(new
        {
            accountTitle = "Creá tu cuenta",
            connectTitle = "Conectar Mercado Pago",
            alertsTitle = "Activá los avisos",
            iosHint = "En el iPhone, agregá la app a la pantalla de inicio.",
            trialLine = "La prueba está en curso.",
            help = Help,
            button = "Conectar Mercado Pago",
            accountDone = true,
            connectDone = connection is not null,
            alertsDone = push,
            trialRunning = merchant?.TrialEndsAt is { } trial && now < trial,
            listReachable = connection is not null,
            currentStep = step,
            videoUrl = onboarding.Value.MercadoPagoVideoUrl ?? ""
        });
    }

    private static bool Owner(HttpContext http) => http.User.FindFirst("owner")?.Value == "1";

    private static LoginAccount Account(HttpContext http) => (LoginAccount)http.Items["account"]!;
}
