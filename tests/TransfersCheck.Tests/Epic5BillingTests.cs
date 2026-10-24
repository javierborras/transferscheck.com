using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic5BillingTests(SqlFixture sql)
{
    [Fact]
    public async Task The_pay_screen_reads_pack_evento_and_pack_mes_from_the_plan_table()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var email = $"pay-{Guid.NewGuid():N}@counter.test";
        var cookie = await SignUp(client, email);
        using var priced = await Send(client, "/api/billing", cookie, HttpMethod.Get);
        var body = await priced.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Email", body.GetProperty("accountLabel").GetString());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Plan", body.GetProperty("planLabel").GetString());
        Assert.False(body.TryGetProperty("subscriptionTitle", out _));
        Assert.DoesNotContain("Suscripción", body.ToString(), StringComparison.Ordinal);

        var plans = body.GetProperty("plans").EnumerateArray().ToArray();
        Assert.True(plans.Length >= 2);
        var evento = plans.Single(plan => plan.GetProperty("code").GetString() == PlanCodes.PackEvento);
        Assert.Equal(1000, evento.GetProperty("amount").GetInt32());
        Assert.Equal("ARS", evento.GetProperty("currency").GetString());
        Assert.Equal(24, evento.GetProperty("durationHours").GetInt32());
        Assert.Equal(
            "Un día, un usuario. Ideal para la fiesta o el show.",
            evento.GetProperty("faceCopy").GetString());
        Assert.False(evento.GetProperty("enableCopilotHelp").GetBoolean());

        var mes = plans.Single(plan => plan.GetProperty("code").GetString() == PlanCodes.PackMes);
        Assert.Equal(5999, mes.GetProperty("amount").GetInt32());
        Assert.Equal(30, mes.GetProperty("durationDays").GetInt32());
        Assert.True(mes.GetProperty("enableCopilotHelp").GetBoolean());

        Assert.False(body.TryGetProperty("amount", out _));
        Assert.DoesNotContain(plans, plan => plan.GetProperty("code").GetString() is "50" or "100" or "500" or "1000+");

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("Email", app, StringComparison.Ordinal);
        Assert.Contains("Plan", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Suscripción", app, StringComparison.Ordinal);
        Assert.DoesNotContain("quantity", app, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pack_mes_trial_starts_at_the_mercado_pago_link_and_ends_21_days_later()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await SignUp(client);
        await SetPlanCode(factory, cookie, PlanCodes.PackMes);

        Assert.Null(await AccessUntil(client, cookie));
        using var scopeBefore = factory.Services.CreateScope();
        var tenantBefore = scopeBefore.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantBefore.TenantId = await Tenant(client, cookie);
        var storeBefore = scopeBefore.ServiceProvider.GetRequiredService<IMerchantStore>();
        var merchantBefore = await storeBefore.FindMerchantAsync(tenantBefore.TenantId, CancellationToken.None);
        Assert.Null(merchantBefore!.TrialEndsAt);
        Assert.Null(merchantBefore.ConnectedAt);

        var linkAt = clock.Now;
        using var done = await Connect(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);

        var access = await AccessUntil(client, cookie);
        Assert.Equal(linkAt.AddDays(21), access);

        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = await Tenant(client, cookie);
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var merchant = await store.FindMerchantAsync(tenant.TenantId, CancellationToken.None);
        // ConnectedAt is the Mercado Pago link instant and the seven-day usage period start (wave 22).
        Assert.Equal(linkAt, merchant!.ConnectedAt);
        Assert.Equal(linkAt.AddDays(21), merchant.TrialEndsAt);
        Assert.Equal(merchant.TrialEndsAt, merchant.AccessUntil);
        Assert.Equal(linkAt.AddDays(21), merchant.AccessUntil);
    }

    [Fact]
    public async Task Day_15_stops_sync_and_the_list_stays()
    {
        // Wave 12 replaces this withdrawn day-15 stop; keep suite green with Pack Mes 21-day trial.
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"day15-{Guid.NewGuid():N}@counter.test";
        var cookie = await ConnectOwner(client, factory, email);
        using var synced = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, synced.StatusCode);

        clock.Now = clock.Now.AddDays(21);
        using var again = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1" });
        again.EnsureSuccessStatusCode();
        cookie = again.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        MockState.Payments.Add(MockState.Payment(80, "approved", "bank_transfer", 80m, "Dia Quince"));
        using var stopped = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.Conflict, stopped.StatusCode);
        var error = await stopped.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_entitled", error.GetProperty("error").GetString());

        using var list = await Send(client, "/api/transfers", cookie, HttpMethod.Get);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(10, items.Length);
        Assert.DoesNotContain(items, item => item.GetProperty("payerName").GetString()!.StartsWith("Dia Quince", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Payment_link_extends_access_without_cashback_and_return_does_not()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"pay-link-{Guid.NewGuid():N}@counter.test";
        var cookie = await ConnectOwner(client, factory, email);
        var tenantId = await Tenant(client, cookie);
        var before = await AccessUntil(client, cookie);
        Assert.NotNull(before);
        Assert.True(before > clock.Now);

        var appsettings = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Api", "appsettings.json"));
        Assert.Contains("\"AccessToken\": \"\"", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("cashback", appsettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coupon", appsettings, StringComparison.OrdinalIgnoreCase);

        using var checkout = await Send(client, "/api/billing/checkout", cookie);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var initPoint = (await checkout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("initPoint").GetString();
        Assert.Contains("pref-1", initPoint, StringComparison.Ordinal);
        Assert.Equal("Bearer mock-checkout-token", MockState.LastCheckoutAuthorization);
        Assert.DoesNotContain("preapproval", MockState.LastCheckoutBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auto_recurring", MockState.LastCheckoutBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cashback", MockState.LastCheckoutBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coupon", MockState.LastCheckoutBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external_reference", MockState.LastCheckoutBody, StringComparison.Ordinal);

        using var returned = await client.GetAsync("/api/billing/return");
        Assert.Equal(HttpStatusCode.Redirect, returned.StatusCode);
        Assert.Equal(before, await AccessUntil(client, cookie));

        using var denied = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
        denied.Content = JsonContent.Create(new { data = new { id = "pay-denied" } });
        using var deniedResponse = await client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedResponse.StatusCode);

        MockState.CheckoutTenants["pay-pending"] = tenantId.ToString();
        using var pending = Webhook(client, "unpaid-1");
        using var pendingResponse = await client.SendAsync(pending);
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        Assert.Equal(before, await AccessUntil(client, cookie));

        // Pay while days remain: period continues from AccessUntil without a gap.
        var paymentId = "pay-" + Guid.NewGuid().ToString("N");
        MockState.CheckoutTenants[paymentId] = tenantId.ToString();
        using var applied = await client.SendAsync(Webhook(client, paymentId));
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var extended = await AccessUntil(client, cookie);
        Assert.Equal(before.Value.AddDays(30), extended);

        using var replay = await client.SendAsync(Webhook(client, paymentId));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(extended, await AccessUntil(client, cookie));

        // Pay after the block: new period starts at payment.
        clock.Now = extended!.Value.AddDays(2);
        using var again = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1" });
        again.EnsureSuccessStatusCode();
        cookie = again.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var afterBlockId = "pay-" + Guid.NewGuid().ToString("N");
        MockState.CheckoutTenants[afterBlockId] = tenantId.ToString();
        using var afterBlock = await client.SendAsync(Webhook(client, afterBlockId));
        Assert.Equal(HttpStatusCode.OK, afterBlock.StatusCode);
        Assert.Equal(clock.Now.AddDays(30), await AccessUntil(client, cookie));

        var core = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Core", "Ports.cs"));
        Assert.DoesNotContain("Cashback", core, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Coupon", core, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static HttpRequestMessage Webhook(HttpClient client, string paymentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
        request.Headers.TryAddWithoutValidation("X-Webhook-Secret", "test-webhook-secret");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { type = "payment", data = new { id = paymentId } }),
            Encoding.UTF8,
            "application/json");
        return request;
    }

    private static async Task<DateTimeOffset?> AccessUntil(HttpClient client, string cookie)
    {
        using var response = await Send(client, "/api/billing", cookie, HttpMethod.Get);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessUntil").ValueKind == JsonValueKind.Null
            ? null
            : body.GetProperty("accessUntil").GetDateTimeOffset();
    }

    private static async Task<Guid> Tenant(HttpClient client, string cookie)
    {
        using var response = await Send(client, "/api/session", cookie, HttpMethod.Get);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("tenantId").GetGuid();
    }

    private static async Task<string> SignUp(HttpClient client, string? email = null)
    {
        using var signup = await client.PostAsJsonAsync("/api/accounts", new
        {
            email = email ?? $"pay-{Guid.NewGuid():N}@counter.test",
            password = "password1"
        });
        signup.EnsureSuccessStatusCode();
        return signup.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
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

    private static async Task<string> ConnectOwner(HttpClient client, TransfersApiFactory factory, string? email = null)
    {
        var cookie = await SignUp(client, email);
        await SetPlanCode(factory, cookie, PlanCodes.PackMes);
        using var done = await Connect(client, cookie);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        return cookie;
    }

    private static async Task<HttpResponseMessage> Connect(HttpClient client, string cookie)
    {
        using var started = await Send(client, "/api/connections/mercadopago", cookie);
        var authorizationUrl = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var merchantLogin = await browser.GetAsync(authorizationUrl);
        var query = merchantLogin.Headers.Location!.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        return await Send(client, $"/api/connections/mercadopago/callback?code={query["code"]}&state={query["state"]}", cookie, HttpMethod.Get);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return client.SendAsync(request);
    }
}
