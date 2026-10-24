using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Accounts;

/// <summary>
/// Commerce door (ADR-018): Auth0 Universal Login identifies the admin, then this API issues the
/// same local session cookie as before. Punto de Alerta and the owner microsite never use it.
/// </summary>
public static class Auth0Endpoints
{
    private const string AttemptCookie = "tc_auth0";

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/auth0");
        group.MapGet("/config", Config);
        group.MapGet("/login", Begin);
        group.MapGet("/callback", Callback);
    }

    private static IResult Config(IOptions<Auth0Options> options)
        => Results.Ok(new { enabled = options.Value.Enabled });

    private static IResult Begin(
        bool? signup,
        string? email,
        IOptions<Auth0Options> options,
        IIdentityProvider identity,
        ITokenProtector protector,
        TimeProvider clock,
        HttpResponse response)
    {
        if (!options.Value.Enabled)
            return Results.NotFound();

        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");
        var verifier = Pkce.Verifier();
        var expires = clock.GetUtcNow().Add(options.Value.AttemptLifetime);
        response.Cookies.Append(AttemptCookie, protector.Protect($"{state}|{verifier}|{nonce}|{expires.ToUnixTimeSeconds()}"), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth0",
            Expires = expires
        });

        return Results.Redirect(identity.BuildAuthorizationUrl(state, Pkce.Challenge(verifier), nonce, signup == true, email));
    }

    private static async Task<IResult> Callback(
        string? code,
        string? state,
        HttpContext http,
        IOptions<Auth0Options> options,
        IIdentityProvider identity,
        ITokenProtector protector,
        IMerchantStore store,
        IOptions<LoginSessionOptions> sessionOptions,
        IOptions<UiOptions> ui,
        ITenantContext tenantContext,
        TimeProvider clock,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Results.NotFound();

        var origin = (ui.Value.Origin ?? "").TrimEnd('/');
        IResult Fail(string error) => Results.Redirect($"{origin}/?auth0_error={error}");

        var attempt = ReadAttempt(http, protector, clock.GetUtcNow());
        http.Response.Cookies.Delete(AttemptCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth0"
        });
        if (attempt is null || string.IsNullOrEmpty(state)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(attempt.Value.State)))
            return Fail("invalid_state");
        if (string.IsNullOrWhiteSpace(code))
            return Fail("access_denied");

        IdentityClaims claims;
        try
        {
            claims = await identity.ExchangeAsync(code, attempt.Value.Verifier, attempt.Value.Nonce, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeouts, a malformed JWKS or id_token, or a bad Authority all end as a login-screen message, never a 500.
            logger.LogWarning(exception, "Auth0 exchange failed.");
            return Fail("exchange_failed");
        }

        if (!claims.EmailVerified)
            return Fail("email_unverified");

        var account = await store.FindAccountByEmailAsync(claims.Email, cancellationToken);
        if (account is null)
        {
            var merchant = new Merchant
            {
                Id = Guid.NewGuid(),
                CreatedAt = clock.GetUtcNow(),
                ChecklistStep = ChecklistRules.Step(true, false, false)
            };
            await store.AddMerchantAsync(merchant, cancellationToken);
            account = new LoginAccount
            {
                Id = Guid.NewGuid(),
                TenantId = merchant.Id,
                Email = claims.Email,
                // No local password: Auth0 owns the credential, so the local door cannot be used for this account.
                PasswordHash = PasswordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                IsOwner = true,
                PasswordSet = false
            };
            await store.AddAccountAsync(account, cancellationToken);
        }
        else if (!account.IsOwner || account.Revoked)
        {
            return Fail("not_allowed");
        }

        await AccountEndpoints.Issue(store, tenantContext, sessionOptions.Value, clock, http.Response, account, cancellationToken);
        return Results.Redirect(string.IsNullOrEmpty(origin) ? "/" : origin + "/");
    }

    private static (string State, string Verifier, string Nonce)? ReadAttempt(HttpContext http, ITokenProtector protector, DateTimeOffset now)
    {
        if (!http.Request.Cookies.TryGetValue(AttemptCookie, out var protectedValue) || string.IsNullOrEmpty(protectedValue))
            return null;

        string plain;
        try
        {
            plain = protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = plain.Split('|');
        if (parts.Length != 4 || !long.TryParse(parts[3], out var expiresAt) || DateTimeOffset.FromUnixTimeSeconds(expiresAt) <= now)
            return null;
        return (parts[0], parts[1], parts[2]);
    }
}
