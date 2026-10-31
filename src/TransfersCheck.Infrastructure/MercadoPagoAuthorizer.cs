using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class MercadoPagoAuthorizer(HttpClient http, IOptions<MercadoPagoOptions> options, IMercadoPagoCallLog? calls = null) : IMerchantAuthorizer
{
    private readonly MercadoPagoOptions _options = options.Value;

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        var query = string.Join("&", new[]
        {
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(_options.ClientId),
            "redirect_uri=" + Uri.EscapeDataString(_options.RedirectUri),
            "code_challenge=" + Uri.EscapeDataString(codeChallenge),
            "code_challenge_method=S256",
            "state=" + Uri.EscapeDataString(state)
        });
        return _options.AuthorizationUrl + "?" + query;
    }

    public Task<TokenSet> ExchangeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
        => TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["code_verifier"] = codeVerifier
        }, cancellationToken);

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, cancellationToken);

    private async Task<TokenSet> TokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        if (calls is not null)
            await calls.RecordAsync(cancellationToken);
        using var response = await http.PostAsync(_options.TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // 400/401/403 means this grant will not start working on the next tick (invalid_grant, revoked, wrong client).
            // The body can echo credential material, so it stays out of the exception.
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new MerchantAuthorizationRejectedException();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new RateLimitedException();
            throw new HttpRequestException($"Mercado Pago token exchange failed ({(int)response.StatusCode}).", null, response.StatusCode);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
        return new TokenSet(
            root.GetProperty("access_token").GetString() ?? "",
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() ?? "" : "",
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            root.TryGetProperty("user_id", out var user) ? user.ToString() : "");
    }
}
