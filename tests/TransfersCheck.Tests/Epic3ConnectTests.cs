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
        await SetPlanCode(factory, cookie, PlanCodes.PackMes);
        using var before = await Send(client, "/api/checklist", cookie, HttpMethod.Get);
        var checklist = await before.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Creá tu cuenta", checklist.GetProperty("accountTitle").GetString());
        Assert.Equal("Conectar Mercado Pago", checklist.GetProperty("connectTitle").GetString());
        Assert.Equal("Conectar Mercado Pago", checklist.GetProperty("button").GetString());
        Assert.Equal("Re-conectar cuenta", checklist.GetProperty("reconnectButton").GetString());
        Assert.Equal("No conectado", checklist.GetProperty("connectionBadge").GetString());
        Assert.Equal(
            "¿Estás seguro que deseas re-conectar tu cuenta de Mercado Pago?",
            checklist.GetProperty("reconnectConfirm").GetString());
        Assert.Equal(ConnectionEndpoints.Help, checklist.GetProperty("help").GetString());
        Assert.Contains("1.", checklist.GetProperty("help").GetString(), StringComparison.Ordinal);
        Assert.Contains("2.", checklist.GetProperty("help").GetString(), StringComparison.Ordinal);
        Assert.Equal("Activá los avisos", checklist.GetProperty("alertsTitle").GetString());
        Assert.Equal("La prueba está en curso.", checklist.GetProperty("trialLine").GetString());
        Assert.False(checklist.TryGetProperty("payTitle", out _));
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
        Assert.Equal("Conectado", ready.GetProperty("connectionBadge").GetString());
        Assert.Equal("Conectar Mercado Pago", ready.GetProperty("button").GetString());
        Assert.Equal("Re-conectar cuenta", ready.GetProperty("reconnectButton").GetString());
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
        var linkedUser = Random.Shared.Next(2000, 2_000_000_000);
        // Mock oauth increments UserId per code exchange; pin the same id for both merchants.
        MockState.UserId = linkedUser - 1;
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var first = await SignUp(client, $"a-{Guid.NewGuid():N}@counter.test");
        using var firstDone = await Connect(client, first);
        Assert.Equal(HttpStatusCode.Redirect, firstDone.StatusCode);

        MockState.UserId = linkedUser - 1;
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

    [Fact]
    public async Task Checklist_exposes_connect_words_trial_status_and_no_client_advance()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await SignUp(client);
        await SetPlanCode(factory, cookie, PlanCodes.PackMes);

        using var before = await Send(client, "/api/checklist", cookie, HttpMethod.Get);
        var open = await before.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Creá tu cuenta", open.GetProperty("accountTitle").GetString());
        Assert.Equal("Conectar Mercado Pago", open.GetProperty("connectTitle").GetString());
        Assert.Equal("Activá los avisos", open.GetProperty("alertsTitle").GetString());
        Assert.Equal("La prueba está en curso.", open.GetProperty("trialLine").GetString());
        Assert.Equal(ConnectionEndpoints.Help, open.GetProperty("help").GetString());
        Assert.Equal("Conectar Mercado Pago", open.GetProperty("button").GetString());
        Assert.Equal("Re-conectar cuenta", open.GetProperty("reconnectButton").GetString());
        Assert.Equal("¿Estás seguro que deseas re-conectar tu cuenta de Mercado Pago?", open.GetProperty("reconnectConfirm").GetString());
        Assert.Equal("No conectado", open.GetProperty("connectionBadge").GetString());
        Assert.Equal("", open.GetProperty("videoUrl").GetString());
        Assert.DoesNotContain("Pagar", open.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chatbot", open.ToString(), StringComparison.OrdinalIgnoreCase);

        using var advance = await Send(client, "/api/checklist", cookie, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, advance.StatusCode);

        using var connected = await Connect(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, connected.StatusCode);
        using var after = await Send(client, "/api/checklist", cookie, HttpMethod.Get);
        var ready = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Conectado", ready.GetProperty("connectionBadge").GetString());
        Assert.True(ready.GetProperty("connectDone").GetBoolean());
        Assert.True(ready.GetProperty("listReachable").GetBoolean());
        Assert.True(ready.GetProperty("trialRunning").GetBoolean());
        Assert.Equal("", ready.GetProperty("videoUrl").GetString());
    }

    [Fact]
    public void Operator_setup_is_the_pkce_checklist_with_no_secret_values()
    {
        var root = RepoRoot();
        var setup = File.ReadAllText(Path.Combine(root, "_bmad-output", "transferscheck", "mp-authorization-setup.md"));
        Assert.Contains("film script", setup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Operator checklist", setup, StringComparison.Ordinal);
        Assert.Contains("Create one application manually", setup, StringComparison.Ordinal);
        Assert.Contains("URLs de redireccionamiento", setup, StringComparison.Ordinal);
        Assert.Contains("Usar el flujo de código de autorización con PKCE", setup, StringComparison.Ordinal);
        Assert.Contains("user-secrets", setup, StringComparison.Ordinal);
        Assert.Contains("Rotate any client id or client secret that was in the old proof", setup, StringComparison.Ordinal);
        Assert.Contains("Confirm with one real OAuth", setup, StringComparison.Ordinal);
        Assert.Contains("Merchants never create a Mercado Pago application", setup, StringComparison.Ordinal);
        Assert.Contains("not shown to a Punto de Alerta", setup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("APP_USR-", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("TrialDays", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("PriceAmount", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("5999", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("already locked", setup, StringComparison.Ordinal);

        var options = File.ReadAllText(Path.Combine(root, "src", "TransfersCheck.Core", "Options.cs"));
        Assert.Contains("DataProtectionPathOptions", options, StringComparison.Ordinal);
        Assert.Contains("KeyPath", options, StringComparison.Ordinal);
        var di = File.ReadAllText(Path.Combine(root, "src", "TransfersCheck.Infrastructure", "DependencyInjection.cs"));
        Assert.Contains("PersistKeysToFileSystem", di, StringComparison.Ordinal);
        Assert.Contains("DataProtection", di, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static async Task SetPlanCode(TransfersApiFactory factory, string cookie, string planCode)
    {
        using var client = factory.CreateClient();
        using var session = await Send(client, "/api/session", cookie, HttpMethod.Get);
        var body = await session.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = body.GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var merchant = await store.FindMerchantAsync(tenantId, CancellationToken.None);
        Assert.NotNull(merchant);
        merchant!.PlanCode = planCode;
        await store.SaveChangesAsync(CancellationToken.None);
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
