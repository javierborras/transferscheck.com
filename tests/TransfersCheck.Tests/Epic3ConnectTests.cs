using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TransfersCheck.Api.Connections;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic3ConnectTests(SqlFixture sql)
{
    [Fact]
    public async Task Connect_uses_pkce_and_an_expired_state_is_rejected()
    {
        MockState.Reset();
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await SignUp(client);

        using var started = await Send(client, "/api/connections/mercadopago", cookie);
        var startedBody = await started.Content.ReadFromJsonAsync<JsonElement>();
        var authorizationUrl = startedBody.GetProperty("authorizationUrl").GetString()!;
        Assert.Contains("code_challenge_method=S256", authorizationUrl, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", authorizationUrl, StringComparison.Ordinal);
        Assert.Contains("state=", authorizationUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("code_method=", authorizationUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("platform_id=", authorizationUrl, StringComparison.Ordinal);

        clock.Now = clock.Now.AddMinutes(11);
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var merchantLogin = await browser.GetAsync(authorizationUrl);
        var query = Query(merchantLogin.Headers.Location!.Query);
        var code = query["code"];
        var state = query["state"];
        using var expired = await Send(client, $"/api/connections/mercadopago/callback?code={code}&state={state}", cookie, HttpMethod.Get);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
    }

    [Fact]
    public async Task Callback_stores_an_encrypted_connection_starts_the_trial_and_opens_the_list()
    {
        MockState.Reset();
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await SignUp(client);
        using var before = await Send(client, "/api/checklist", cookie, HttpMethod.Get);
        var checklist = await before.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Creá tu cuenta", checklist.GetProperty("accountTitle").GetString());
        Assert.Equal("Conectar Mercado Pago", checklist.GetProperty("button").GetString());
        Assert.Equal(ConnectionEndpoints.Help, checklist.GetProperty("help").GetString());
        Assert.Equal("Activá los avisos", checklist.GetProperty("alertsTitle").GetString());
        Assert.Equal("La prueba está en curso.", checklist.GetProperty("trialLine").GetString());
        Assert.True(checklist.GetProperty("accountDone").GetBoolean());
        Assert.False(checklist.GetProperty("connectDone").GetBoolean());
        Assert.False(checklist.GetProperty("trialRunning").GetBoolean());
        Assert.False(checklist.GetProperty("listReachable").GetBoolean());
        Assert.Equal("", checklist.GetProperty("videoUrl").GetString());

        using var done = await Connect(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        using var after = await Send(client, "/api/checklist", cookie, HttpMethod.Get);
        var ready = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ready.GetProperty("connectDone").GetBoolean());
        Assert.True(ready.GetProperty("listReachable").GetBoolean());
        Assert.True(ready.GetProperty("trialRunning").GetBoolean());
        Assert.Equal("alerts", ready.GetProperty("currentStep").GetString());

        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = (await CurrentTenant(client, cookie));
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var connection = await store.FindConnectionAsync(CancellationToken.None);
        Assert.NotNull(connection);
        Assert.DoesNotContain("mock-access-token", connection.AccessTokenProtected, StringComparison.Ordinal);
        var merchant = await store.FindMerchantAsync(tenant.TenantId, CancellationToken.None);
        Assert.NotNull(merchant!.AccessUntil);
        Assert.Equal(merchant.TrialEndsAt, merchant.AccessUntil);
    }

    [Fact]
    public async Task A_mercado_pago_account_already_linked_is_refused()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var first = await SignUp(client, $"a-{Guid.NewGuid():N}@counter.test");
        using var firstDone = await Connect(client, first);
        Assert.Equal(HttpStatusCode.Redirect, firstDone.StatusCode);

        var second = await SignUp(client, $"b-{Guid.NewGuid():N}@counter.test");
        using var secondDone = await Connect(client, second);
        Assert.Equal(HttpStatusCode.Conflict, secondDone.StatusCode);
    }

    [Fact]
    public async Task A_video_url_is_returned_and_an_empty_one_stays_empty()
    {
        await using var factory = new TransfersApiFactory(sql) { VideoUrl = "https://example.com/consent" };
        using var client = factory.CreateClient();
        var cookie = await SignUp(client);
        using var response = await Send(client, "/api/checklist", cookie, HttpMethod.Get);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://example.com/consent", body.GetProperty("videoUrl").GetString());
    }

    [Fact]
    public async Task The_client_cannot_advance_the_checklist()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var cookie = await SignUp(client);
        using var response = await Send(client, "/api/checklist", cookie, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Connect(HttpClient client, string cookie)
    {
        using var started = await Send(client, "/api/connections/mercadopago", cookie);
        var authorizationUrl = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var merchantLogin = await browser.GetAsync(authorizationUrl);
        var query = Query(merchantLogin.Headers.Location!.Query);
        return await Send(client, $"/api/connections/mercadopago/callback?code={query["code"]}&state={query["state"]}", cookie, HttpMethod.Get);
    }

    private static async Task<string> SignUp(HttpClient client, string? email = null)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            email = email ?? $"owner-{Guid.NewGuid():N}@counter.test",
            password = "password1"
        });
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private static async Task<Guid> CurrentTenant(HttpClient client, string cookie)
    {
        using var response = await Send(client, "/api/session", cookie, HttpMethod.Get);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("tenantId").GetGuid();
    }

    private static Dictionary<string, string> Query(string query)
        => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return client.SendAsync(request);
    }
}
