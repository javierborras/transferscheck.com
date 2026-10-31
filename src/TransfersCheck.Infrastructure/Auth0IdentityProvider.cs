using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

/// <summary>
/// Auth0 Universal Login (authorization code + PKCE). The id_token is validated here
/// (RS256 signature from the tenant JWKS, issuer, audience, expiry, nonce) before any claim is trusted.
/// </summary>
public sealed class Auth0IdentityProvider(HttpClient http, IOptions<Auth0Options> options, TimeProvider clock) : IIdentityProvider
{
    private static readonly TimeSpan JwksLifetime = TimeSpan.FromHours(1);
    private static readonly SemaphoreSlim JwksLock = new(1, 1);
    private static readonly Dictionary<string, (JsonWebKeySet Keys, DateTimeOffset FetchedAt)> JwksCache = new();

    private readonly Auth0Options _options = options.Value;

    private string Root => _options.Authority.TrimEnd('/');
    private string Issuer => Root + "/";

    public string BuildAuthorizationUrl(string state, string codeChallenge, string nonce, bool signUp, string? loginHint)
    {
        var parts = new List<string>
        {
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(_options.ClientId),
            "redirect_uri=" + Uri.EscapeDataString(_options.RedirectUri),
            "scope=" + Uri.EscapeDataString("openid email profile"),
            "code_challenge=" + Uri.EscapeDataString(codeChallenge),
            "code_challenge_method=S256",
            "state=" + Uri.EscapeDataString(state),
            "nonce=" + Uri.EscapeDataString(nonce)
        };
        if (signUp)
            parts.Add("screen_hint=signup");
        if (!string.IsNullOrWhiteSpace(loginHint))
            parts.Add("login_hint=" + Uri.EscapeDataString(loginHint));
        return Root + "/authorize?" + string.Join("&", parts);
    }

    public string BuildLogoutUrl(string returnTo)
    {
        var parts = new List<string>
        {
            "client_id=" + Uri.EscapeDataString(_options.ClientId),
            "returnTo=" + Uri.EscapeDataString(returnTo)
        };
        return Root + "/v2/logout?" + string.Join("&", parts);
    }

    public async Task<IdentityClaims> ExchangeAsync(string code, string codeVerifier, string nonce, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["code_verifier"] = codeVerifier
        };
        using var response = await http.PostAsync(Root + "/oauth/token", new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Auth0 token exchange failed ({(int)response.StatusCode}).");

        string idToken;
        try
        {
            using var doc = JsonDocument.Parse(body);
            idToken = doc.RootElement.GetProperty("id_token").GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("Auth0 token response had no id_token.");
        }

        var result = await ValidateAsync(idToken, forceRefresh: false, cancellationToken);
        if (!result.IsValid)
            result = await ValidateAsync(idToken, forceRefresh: true, cancellationToken);
        if (!result.IsValid)
            throw new InvalidOperationException($"Auth0 id_token is not valid: {result.Exception?.GetType().Name}: {result.Exception?.Message}");

        var claims = result.Claims;
        if (!claims.TryGetValue("nonce", out var tokenNonce) || !string.Equals(tokenNonce?.ToString(), nonce, StringComparison.Ordinal))
            throw new InvalidOperationException("Auth0 id_token nonce mismatch.");

        var subject = claims.TryGetValue("sub", out var sub) ? sub?.ToString() ?? "" : "";
        var email = claims.TryGetValue("email", out var mail) ? mail?.ToString() ?? "" : "";
        if (subject.Length == 0 || email.Length == 0)
            throw new InvalidOperationException("Auth0 id_token has no subject or email.");

        var verified = claims.TryGetValue("email_verified", out var flag)
            && (flag is true || string.Equals(flag?.ToString(), "true", StringComparison.OrdinalIgnoreCase));
        return new IdentityClaims(subject, email, verified);
    }

    private async Task<TokenValidationResult> ValidateAsync(string idToken, bool forceRefresh, CancellationToken cancellationToken)
    {
        var keys = await KeysAsync(forceRefresh, cancellationToken);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = keys.GetSigningKeys(),
            ClockSkew = TimeSpan.FromMinutes(1),
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var skew = TimeSpan.FromMinutes(1);
                return expires is not null && now <= expires.Value + skew && (notBefore is null || now >= notBefore.Value - skew);
            }
        };
        return await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters);
    }

    private async Task<JsonWebKeySet> KeysAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await JwksLock.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            if (!forceRefresh
                && JwksCache.TryGetValue(Root, out var cached)
                && now - cached.FetchedAt < JwksLifetime)
                return cached.Keys;

            using var response = await http.GetAsync(Root + "/.well-known/jwks.json", cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Auth0 JWKS could not be read.");
            var set = new JsonWebKeySet(await response.Content.ReadAsStringAsync(cancellationToken));
            JwksCache[Root] = (set, now);
            return set;
        }
        finally
        {
            JwksLock.Release();
        }
    }
}
