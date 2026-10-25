using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransfersCheck.MockServer;

/// <summary>
/// Test double of an Auth0 tenant (ADR-018): Universal Login page, authorization code + PKCE,
/// and a real RS256 id_token with a JWKS endpoint. Authority for tests is http://127.0.0.1:5099/auth0.
/// Rule: an email starting with "unverified." gets email_verified=false.
/// </summary>
public static class Auth0Mock
{
    public const string ClientId = "mock-auth0-client";
    public const string ClientSecret = "mock-auth0-secret";

    private sealed record Grant(string Challenge, string Email, string Nonce, string RedirectUri);

    private static readonly RSA Key = RSA.Create(2048);
    private static readonly string KeyId = "mock-auth0-key";
    private static readonly ConcurrentDictionary<string, Grant> Grants = new();
    private static readonly ConcurrentDictionary<string, bool> Used = new();

    public static void Reset()
    {
        Grants.Clear();
        Used.Clear();
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/auth0/v2/logout", (string returnTo) => Results.Redirect(returnTo));
        app.MapGet("/auth0/authorize", Authorize);
        app.MapPost("/auth0/oauth/token", Token);
        app.MapGet("/auth0/.well-known/jwks.json", Jwks);
    }

    private static IResult Authorize(HttpRequest request)
    {
        var query = request.Query;
        if (query["response_type"] != "code" || query["client_id"] != ClientId)
            return Results.BadRequest(new { error = "invalid_client" });
        if (query["code_challenge_method"] != "S256" || string.IsNullOrEmpty(query["code_challenge"]))
            return Results.BadRequest(new { error = "pkce_required" });
        if (string.IsNullOrEmpty(query["redirect_uri"]) || string.IsNullOrEmpty(query["state"]) || string.IsNullOrEmpty(query["nonce"]))
            return Results.BadRequest(new { error = "invalid_request" });

        var email = query["login_hint"].ToString().Trim().ToLowerInvariant();
        if (email.Length == 0)
            return Results.Content(LoginPage(request), "text/html; charset=utf-8");

        var code = Guid.NewGuid().ToString("N");
        var redirect = query["redirect_uri"].ToString();
        Grants[code] = new Grant(query["code_challenge"].ToString(), email, query["nonce"].ToString(), redirect);
        var location = $"{redirect}{(redirect.Contains('?') ? "&" : "?")}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(query["state"].ToString())}";
        return Results.Redirect(location);
    }

    private static async Task<IResult> Token(HttpRequest request)
    {
        var form = await request.ReadFormAsync();
        var code = form["code"].ToString();
        if (form["grant_type"] != "authorization_code" || !Grants.TryGetValue(code, out var grant) || !Used.TryAdd(code, true))
            return Results.BadRequest(new { error = "invalid_grant" });
        if (form["client_id"] != ClientId || form["client_secret"] != ClientSecret)
            return Results.BadRequest(new { error = "invalid_client" });
        if (form["redirect_uri"] != grant.RedirectUri
            || Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(form["code_verifier"].ToString()))) != grant.Challenge)
            return Results.BadRequest(new { error = "invalid_grant" });

        var issuer = $"{request.Scheme}://{request.Host}/auth0/";
        var now = DateTimeOffset.UtcNow;
        var subject = "auth0|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grant.Email)))[..12].ToLowerInvariant();
        var idToken = Sign(new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["aud"] = ClientId,
            ["sub"] = subject,
            ["email"] = grant.Email,
            ["email_verified"] = !grant.Email.StartsWith("unverified.", StringComparison.Ordinal),
            ["nonce"] = grant.Nonce,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds()
        });
        return Results.Json(new
        {
            access_token = "mock-auth0-access-" + Guid.NewGuid().ToString("N"),
            id_token = idToken,
            token_type = "Bearer",
            expires_in = 600
        });
    }

    private static IResult Jwks()
    {
        var parameters = Key.ExportParameters(false);
        return Results.Json(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = KeyId,
                    n = Base64Url(parameters.Modulus!),
                    e = Base64Url(parameters.Exponent!)
                }
            }
        });
    }

    private static string Sign(Dictionary<string, object> claims)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = KeyId }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        byte[] signature;
        lock (Key) // RSA instances are not safe for concurrent use across parallel e2e logins.
            signature = Key.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private static string LoginPage(HttpRequest request)
    {
        var hidden = new StringBuilder();
        foreach (var pair in request.Query)
            hidden.Append($"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(pair.Key)}\" value=\"{WebUtility.HtmlEncode(pair.Value.ToString())}\">");
        var signUp = request.Query["screen_hint"] == "signup";
        return $$"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><title>Auth0 simulado</title></head>
            <body>
            <h1>{{(signUp ? "Auth0 simulado: registrarse" : "Auth0 simulado: iniciar sesión")}}</h1>
            <form method="get" action="/auth0/authorize">
            {{hidden}}
            <label>Email <input name="login_hint" type="email" required></label>
            <button type="submit">Continuar</button>
            </form>
            </body></html>
            """;
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
