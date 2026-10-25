using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Api.Transfers;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic4SyncTests(SqlFixture sql)
{
    [Fact]
    public async Task Refresh_keeps_the_last_10_of_the_seeded_types_once()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        MockState.Payments.Add(MockState.Payment(99, "approved", "credit_card", 5m, "Tarjeta"));
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock, PageSize = 20 };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await ConnectOwner(client, factory);

        using var first = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        clock.Now = clock.Now.AddSeconds(61);
        using var third = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);

        using var list = await Send(client, "/api/transfers", cookie, HttpMethod.Get);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(10, items.Length);
        Assert.All(items, item => Assert.NotEqual("credit_card", item.GetProperty("typeCode").GetString()));
        Assert.Contains(items, item => item.GetProperty("statusLabel").GetString() == "Llegó");
        Assert.Contains(items, item => item.GetProperty("statusLabel").GetString() == "Pendiente");
        Assert.Contains(items, item => item.GetProperty("statusLabel").GetString() == "Rechazado");
        Assert.Contains(items, item => item.GetProperty("payerName").GetString()!.StartsWith("Persona", StringComparison.Ordinal));
        Assert.Contains(items, item => item.GetProperty("amount").GetDecimal() > 0);
        Assert.All(items, item =>
        {
            Assert.True(item.TryGetProperty("occurredAt", out _));
            Assert.True(item.TryGetProperty("payerName", out _));
            Assert.True(item.TryGetProperty("amount", out _));
            Assert.True(item.TryGetProperty("statusLabel", out _));
            Assert.False(item.TryGetProperty("medio", out _));
            Assert.False(item.TryGetProperty("paymentMethod", out _));
        });
        Assert.Equal(300, body.GetProperty("pollIntervalSeconds").GetInt32());
        Assert.Equal(60, body.GetProperty("buttonCooldownSeconds").GetInt32());
    }

    [Fact]
    public async Task Empty_payer_name_is_desconocido_and_the_ui_has_no_sincronizar()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        MockState.Payments.Clear();
        MockState.Payments.Add(MockState.Payment(1, "approved", "bank_transfer", 10m, ""));
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await ConnectOwner(client, factory);
        using var refreshed = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using var list = await Send(client, "/api/transfers", cookie, HttpMethod.Get);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("payerName").GetString() == "Desconocido");

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.DoesNotContain("Sincronizar", app, StringComparison.Ordinal);
        Assert.Contains("🔄 Revisar si entró un pago ahora", app, StringComparison.Ordinal);
        Assert.Contains("Fecha", app, StringComparison.Ordinal);
        Assert.Contains("Pagador", app, StringComparison.Ordinal);
        Assert.Contains("Monto", app, StringComparison.Ordinal);
        Assert.Contains("Estado", app, StringComparison.Ordinal);
        Assert.Contains(
            "TransferPollWorker",
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Api", "Program.cs")),
            StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    [Fact]
    public async Task Mercado_Pago_429_and_2001_wait_until_the_next_round()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await ConnectOwner(client, factory);
        MockState.RateLimit = "429";
        using var limited = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        MockState.RateLimit = "";
        var searches = MockState.SearchCount;
        using var waiting = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.TooManyRequests, waiting.StatusCode);
        Assert.Equal(searches, MockState.SearchCount);

        clock.Now = clock.Now.AddMinutes(5);
        using var again = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        MockState.RateLimit = "2001";
        clock.Now = clock.Now.AddMinutes(2);
        using var coded = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.TooManyRequests, coded.StatusCode);
    }

    [Fact]
    public async Task A_replica_without_the_lock_skips_the_poll()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await ConnectOwner(client, factory);
        using var scope = factory.Services.CreateScope();
        var poll = scope.ServiceProvider.GetRequiredService<IPollLock>();
        var hold = await poll.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(hold);
        var before = MockState.SearchCount;
        var worker = factory.Services.GetRequiredService<TransferPollWorker>();
        await worker.PollOnce(CancellationToken.None);
        Assert.Equal(before, MockState.SearchCount);
        await hold.DisposeAsync();
        await worker.PollOnce(CancellationToken.None);
        Assert.True(MockState.SearchCount > before);
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

    private static async Task<string> ConnectOwner(HttpClient client, TransfersApiFactory factory)
    {
        using var signup = await client.PostAsJsonAsync("/api/accounts", new
        {
            email = $"sync-{Guid.NewGuid():N}@counter.test",
            password = "password1"
        });
        signup.EnsureSuccessStatusCode();
        var cookie = signup.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        await SetPlanCode(factory, cookie, PlanCodes.PackMes);
        using var started = await Send(client, "/api/connections/mercadopago", cookie);
        var authorizationUrl = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var merchantLogin = await browser.GetAsync(authorizationUrl);
        var query = merchantLogin.Headers.Location!.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        using var callback = await Send(client, $"/api/connections/mercadopago/callback?code={query["code"]}&state={query["state"]}", cookie, HttpMethod.Get);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return cookie;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return client.SendAsync(request);
    }
}
