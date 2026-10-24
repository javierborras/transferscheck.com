using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Api.Jobs;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic8ReferralTests(SqlFixture sql)
{
    [Fact]
    public async Task Referral_code_is_mi_codigo_full_price_and_hangfire_grants_one_free_pack_mes()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var referrerCookie = await SignUp(client, $"ref-{Guid.NewGuid():N}@counter.test");
        await SetAccess(factory, referrerCookie, clock.Now.AddDays(10));
        using var codeResponse = await Send(client, "/api/referral", referrerCookie, HttpMethod.Get);
        var codeBody = await codeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Mi código", codeBody.GetProperty("label").GetString());
        var code = codeBody.GetProperty("code").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Equal(JsonValueKind.Null, codeBody.GetProperty("cashbackBalance").ValueKind);

        var inviteeCookie = await SignUp(client, $"inv-{Guid.NewGuid():N}@counter.test");
        using var checkout = await Send(client, "/api/billing/checkout", inviteeCookie, json: new { referralCode = code });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var checkoutBody = await checkout.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5999, checkoutBody.GetProperty("amount").GetInt32());

        var inviteeTenant = await TenantId(client, inviteeCookie);
        await ApplyWebhook(client, inviteeTenant, "pay-1");
        using var scope = factory.Services.CreateScope();
        Assert.DoesNotContain("TransferPollWorker", typeof(ReferralBonusJob).Name, StringComparison.Ordinal);

        var referrerBeforeSecond = await AccessUntil(factory, referrerCookie);
        Assert.Equal(clock.Now.AddDays(10).AddDays(30), referrerBeforeSecond);

        var secondInvitee = await SignUp(client, $"inv2-{Guid.NewGuid():N}@counter.test");
        using var checkout2 = await Send(client, "/api/billing/checkout", secondInvitee, json: new { referralCode = code });
        checkout2.EnsureSuccessStatusCode();
        await ApplyWebhook(client, await TenantId(client, secondInvitee), "pay-2");
        Assert.Equal(referrerBeforeSecond, await AccessUntil(factory, referrerCookie));

        var blockedReferrer = await SignUp(client, $"blocked-{Guid.NewGuid():N}@counter.test");
        using var blockedCodeResponse = await Send(client, "/api/referral", blockedReferrer, HttpMethod.Get);
        var blockedCode = (await blockedCodeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        await SetAccess(factory, blockedReferrer, clock.Now.AddDays(-1));
        var blockedInvitee = await SignUp(client, $"binv-{Guid.NewGuid():N}@counter.test");
        using var blockedCheckout = await Send(client, "/api/billing/checkout", blockedInvitee, json: new { referralCode = blockedCode });
        blockedCheckout.EnsureSuccessStatusCode();
        await ApplyWebhook(client, await TenantId(client, blockedInvitee), "pay-blocked");
        Assert.True(await AccessUntil(factory, blockedReferrer) <= clock.Now);

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("Mi código", app, StringComparison.Ordinal);
        Assert.DoesNotContain("coupon", app, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cashback", app, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ApplyWebhook(HttpClient client, Guid tenantId, string paymentId)
    {
        MockState.CheckoutTenants[paymentId] = tenantId.ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
        request.Headers.TryAddWithoutValidation("X-Webhook-Secret", "test-webhook-secret");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { type = "payment", data = new { id = paymentId } }),
            Encoding.UTF8,
            "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SetAccess(TransfersApiFactory factory, string cookie, DateTimeOffset accessUntil)
    {
        using var client = factory.CreateClient();
        var tenantId = await TenantId(client, cookie);
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var merchant = await store.FindMerchantAsync(tenantId, CancellationToken.None);
        merchant!.AccessUntil = accessUntil;
        merchant.PlanCode = PlanCodes.PackMes;
        await store.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<DateTimeOffset?> AccessUntil(TransfersApiFactory factory, string cookie)
    {
        using var client = factory.CreateClient();
        var tenantId = await TenantId(client, cookie);
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var merchant = await store.FindMerchantAsync(tenantId, CancellationToken.None);
        return merchant?.AccessUntil;
    }

    private static async Task<Guid> TenantId(HttpClient client, string cookie)
    {
        using var session = await Send(client, "/api/session", cookie, HttpMethod.Get);
        return (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
    }

    private static async Task<string> SignUp(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null, object? json = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (json is not null)
            request.Content = JsonContent.Create(json);
        return client.SendAsync(request);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
