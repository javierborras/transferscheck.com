using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class FirebasePushSender(HttpClient http, IOptions<FirebaseOptions> firebase, IOptions<UiOptions> ui) : IPushSender
{
    public async Task SendArrivedAsync(string token, Guid transferId, string payerName, decimal amount, string currency, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(firebase.Value.MessagingUrl))
            return;

        var title = StatusWords.Label("approved");
        var body = string.Create(CultureInfo.InvariantCulture, $"{payerName} · {amount:0.##}");
        var origin = ui.Value.Origin.TrimEnd('/');
        var link = string.IsNullOrEmpty(origin) ? $"/?first={transferId}" : $"{origin}/?first={transferId}";
        using var request = new HttpRequestMessage(HttpMethod.Post, firebase.Value.MessagingUrl);
        if (!string.IsNullOrWhiteSpace(firebase.Value.ServiceAccountPath))
        {
            var access = await AccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(access))
                return;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        }

        request.Content = JsonContent.Create(new
        {
            message = new
            {
                token,
                notification = new { title, body },
                data = new Dictionary<string, string>
                {
                    ["transferId"] = transferId.ToString(),
                    ["status"] = "approved",
                    ["payerName"] = payerName,
                    ["amount"] = amount.ToString("0.##", CultureInfo.InvariantCulture),
                    ["title"] = title
                },
                webpush = new
                {
                    fcm_options = new { link },
                    notification = new { title, body }
                }
            }
        });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            if (error.Contains("UNREGISTERED", StringComparison.Ordinal)) throw new InvalidPushTokenException();
        }
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(firebase.Value.ServiceAccountPath, cancellationToken));
        var email = doc.RootElement.GetProperty("client_email").GetString() ?? "";
        var key = doc.RootElement.GetProperty("private_key").GetString() ?? "";
        var tokenUri = doc.RootElement.GetProperty("token_uri").GetString() ?? "";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = email,
            scope = firebase.Value.Scope,
            aud = tokenUri,
            iat = now,
            exp = now + 3000
        })));
        var signingInput = Encoding.ASCII.GetBytes(header + "." + payload);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key);
        var signature = Base64Url(rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri);
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = header + "." + payload + "." + signature
        });
        using var tokenResponse = await http.SendAsync(tokenRequest, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
        return tokenDoc.RootElement.GetProperty("access_token").GetString() ?? "";
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
