using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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

    /// <summary>One run of the Auth0 door: login redirect, mock Universal Login, callback.</summary>
    public sealed record Auth0Run(HttpResponseMessage Callback, string? SessionCookie, string Location);

    public static HttpClient Auth0Client(TransfersApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public static async Task<Auth0Run> SignInWithAuth0(
        TransfersApiFactory factory,
        string email,
        bool signUp = false,
        Func<string, string>? tamperCallback = null,
        bool sendStateCookie = true,
        Guid? suppliedTenant = null)
    {
        var client = Auth0Client(factory);
        using var login = await client.GetAsync($"/api/auth0/login?email={Uri.EscapeDataString(email)}&signup={(signUp ? "true" : "false")}&tenantId={suppliedTenant ?? Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var stateCookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("tc_auth0=", StringComparison.Ordinal)).Split(';')[0];

        using var plain = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var mock = await plain.GetAsync(login.Headers.Location!);
        Assert.Equal(HttpStatusCode.Redirect, mock.StatusCode);
        var callbackUri = mock.Headers.Location!;
        var path = callbackUri.PathAndQuery;
        if (tamperCallback is not null)
            path = tamperCallback(path);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (sendStateCookie)
            request.Headers.TryAddWithoutValidation("Cookie", stateCookie);
        var callback = await client.SendAsync(request);
        var session = callback.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(value => value.StartsWith("tc_session=", StringComparison.Ordinal))?.Split(';')[0]
            : null;
        return new Auth0Run(callback, session, callback.Headers.Location?.ToString() ?? "");
    }
}
