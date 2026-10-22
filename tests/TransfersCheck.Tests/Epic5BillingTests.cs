using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic5BillingTests(SqlFixture sql)
{
    [Fact]
    public async Task The_pay_screen_reads_the_configured_price()
    {
        await using var configured = new TransfersApiFactory(sql) { PriceAmount = 4242 };
        using var client = configured.CreateClient();
        var cookie = await SignUp(client);
        using var priced = await Send(client, "/api/billing", cookie, HttpMethod.Get);
        var body = await priced.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4242, body.GetProperty("amount").GetInt32());
        Assert.Equal("ARS", body.GetProperty("currency").GetString());
        Assert.Equal(30, body.GetProperty("paidPeriodDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("accessUntil").ValueKind);
    }

    [Fact]
    public async Task Day_15_stops_sync_and_the_list_stays()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"day15-{Guid.NewGuid():N}@counter.test";
        var cookie = await ConnectOwner(client, email);
        using var synced = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, synced.StatusCode);

        clock.Now = clock.Now.AddDays(15);
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
    public async Task Webhook_extends_access_and_the_browser_return_does_not()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await ConnectOwner(client);
        var tenantId = await Tenant(client, cookie);
        var before = await AccessUntil(client, cookie);

        using var checkout = await Send(client, "/api/billing/checkout", cookie);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var initPoint = (await checkout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("initPoint").GetString();
        Assert.Contains("pref-1", initPoint, StringComparison.Ordinal);
        Assert.Equal("Bearer mock-checkout-token", MockState.LastCheckoutAuthorization);
        Assert.Contains("5999", MockState.LastCheckoutBody, StringComparison.Ordinal);
        Assert.DoesNotContain("preapproval", MockState.LastCheckoutBody, StringComparison.OrdinalIgnoreCase);

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

        var paymentId = "pay-" + Guid.NewGuid().ToString("N");
        MockState.CheckoutTenants[paymentId] = tenantId.ToString();
        using var applied = await client.SendAsync(Webhook(client, paymentId));
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var extended = await AccessUntil(client, cookie);
        Assert.Equal(before!.Value.AddDays(30), extended);

        using var replay = await client.SendAsync(Webhook(client, paymentId));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(extended, await AccessUntil(client, cookie));
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

    private static async Task<string> SignUp(HttpClient client)
    {
        using var signup = await client.PostAsJsonAsync("/api/accounts", new
        {
            email = $"pay-{Guid.NewGuid():N}@counter.test",
            password = "password1"
        });
        signup.EnsureSuccessStatusCode();
        return signup.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private static async Task<string> ConnectOwner(HttpClient client, string? email = null)
    {
        using var signup = await client.PostAsJsonAsync("/api/accounts", new
        {
            email = email ?? $"pay-{Guid.NewGuid():N}@counter.test",
            password = "password1"
        });
        signup.EnsureSuccessStatusCode();
        var cookie = signup.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
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
