using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;

namespace TransfersCheck.Tests;

internal static class TestAuth
{
    public static async Task<string> SignInWithCode(
        HttpClient client,
        TransfersApiFactory factory,
        string email,
        string password = "password1")
    {
        using var challenge = await client.PostAsJsonAsync("/api/sessions", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        var body = await challenge.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("code_required", body.GetProperty("error").GetString());

        var mail = factory.Mail ?? throw new InvalidOperationException("TransfersApiFactory.Mail is required for sign-in.");
        var sent = mail.Sent.LastOrDefault(item => string.Equals(item.To, email.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.To, email, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrEmpty(sent.Body));
        var code = sent.Body.Trim();

        using var signedIn = await client.PostAsJsonAsync("/api/sessions", new { email, password, code });
        signedIn.EnsureSuccessStatusCode();
        return signedIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }
}
