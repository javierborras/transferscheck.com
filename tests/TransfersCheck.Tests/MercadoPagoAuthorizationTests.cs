using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class MercadoPagoAuthorizationTests(SqlFixture sql)
{
    [Fact]
    public async Task Expired_token_whose_refresh_is_rejected_does_not_query_or_retry_as_a_provider_failure()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock, Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (cookie, email) = await ConnectOwner(client, factory);
        await Expire(factory, cookie, clock.Now.AddMinutes(-5));

        using var scope = factory.Services.CreateScope();
        var poll = scope.ServiceProvider.GetRequiredService<IPollLock>();
        var hold = await poll.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(hold);
        await using (hold)
        {
            MockState.RejectRefresh = true;
            try
            {
                var posts = MockState.TokenPosts;
                var searches = MockState.SearchAttempts;

                using var failed = await Send(client, "/api/transfers/refresh", cookie);
                Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
                Assert.Equal("reconnect_required", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
                Assert.Equal(posts + 1, MockState.TokenPosts);
                Assert.Equal(searches, MockState.SearchAttempts);

                clock.Now = clock.Now.AddMinutes(10);
                using var again = await Send(client, "/api/transfers/refresh", cookie);
                Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
                Assert.Equal(posts + 1, MockState.TokenPosts);
                Assert.Equal(searches, MockState.SearchAttempts);

                var tenantId = await TenantId(factory, cookie);
                using var listed = factory.Services.CreateScope();
                var ids = await listed.ServiceProvider.GetRequiredService<IMerchantStore>().ListConnectedTenantIdsAsync(CancellationToken.None);
                Assert.DoesNotContain(tenantId, ids);

                using var checklist = await Send(client, "/api/checklist", cookie, HttpMethod.Get);
                var ready = await checklist.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Hay que volver a vincular", ready.GetProperty("connectionBadge").GetString());
                Assert.False(ready.GetProperty("connectDone").GetBoolean());

                var ownerCookie = await OwnerCookie(client);
                using var clients = await Send(client, "/api/owner/clients", ownerCookie, HttpMethod.Get);
                var row = (await clients.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single(item => item.GetProperty("email").GetString() == email);
                Assert.False(row.GetProperty("connected").GetBoolean());

                var connection = await LoadConnection(factory, cookie);
                Assert.True(connection.ReconnectRequired);
            }
            finally
            {
                MockState.RejectRefresh = false;
            }
        }
    }

    [Fact]
    public async Task A_401_refreshes_once_and_a_second_401_stops_without_a_technical_cooldown()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (cookie, _) = await ConnectOwner(client, factory);

        using var scope = factory.Services.CreateScope();
        var poll = scope.ServiceProvider.GetRequiredService<IPollLock>();
        var hold = await poll.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(hold);
        await using (hold)
        {
            MockState.RateLimit = "401";
            try
            {
                var posts = MockState.TokenPosts;
                var searches = MockState.SearchAttempts;
                var found = MockState.SearchCount;
                using var failed = await Send(client, "/api/transfers/refresh", cookie);
                Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
                Assert.Equal("reconnect_required", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
                Assert.Equal(posts + 1, MockState.TokenPosts);
                Assert.Equal(searches + 2, MockState.SearchAttempts);
                Assert.Equal(found, MockState.SearchCount);

                clock.Now = clock.Now.AddMinutes(10);
                using var again = await Send(client, "/api/transfers/refresh", cookie);
                Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
                Assert.Equal(posts + 1, MockState.TokenPosts);
                Assert.Equal(searches + 2, MockState.SearchAttempts);

                var merchant = await LoadMerchant(factory, cookie);
                Assert.Null(merchant.RateLimitedUntil);
                Assert.Null(merchant.RefreshNotBefore);
            }
            finally
            {
                MockState.RateLimit = "";
            }
        }
    }

    [Fact]
    public async Task A_single_401_uses_the_refresh_token_and_then_reads_the_payments()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (cookie, _) = await ConnectOwner(client, factory);

        using var scope = factory.Services.CreateScope();
        var poll = scope.ServiceProvider.GetRequiredService<IPollLock>();
        var hold = await poll.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(hold);
        await using (hold)
        {
            MockState.UnauthorizedSearchesRemaining = 1;
            try
            {
                var posts = MockState.TokenPosts;
                var searches = MockState.SearchAttempts;
                var found = MockState.SearchCount;
                using var ok = await Send(client, "/api/transfers/refresh", cookie);
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                Assert.Equal(posts + 1, MockState.TokenPosts);
                Assert.Equal(searches + 2, MockState.SearchAttempts);
                Assert.Equal(found + 1, MockState.SearchCount);
                Assert.False((await LoadConnection(factory, cookie)).ReconnectRequired);
            }
            finally
            {
                MockState.UnauthorizedSearchesRemaining = 0;
            }
        }
    }

    [Fact]
    public async Task Unreadable_stored_tokens_require_reconnect_and_do_not_throw()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (cookie, _) = await ConnectOwner(client, factory);
        var tenantId = await TenantId(factory, cookie);

        var keyDir = Path.Combine(Path.GetTempPath(), "transferscheck-foreign-keys", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        var foreign = DataProtectionProvider.Create(new DirectoryInfo(keyDir))
            .CreateProtector("TransfersCheck.Tokens.v1")
            .Protect("stale-access");

        using (var prepare = factory.Services.CreateScope())
        {
            var tenant = prepare.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.TenantId = tenantId;
            var protector = prepare.ServiceProvider.GetRequiredService<ITokenProtector>();
            Assert.Throws<CryptographicException>(() => protector.Unprotect(foreign));
            var db = prepare.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            var connection = await db.Connections.SingleAsync();
            connection.AccessTokenProtected = foreign;
            connection.RefreshTokenProtected = foreign;
            var merchant = await db.Merchants.SingleAsync();
            merchant.RefreshNotBefore = clock.Now.AddMinutes(-1);
            merchant.RateLimitedUntil = clock.Now.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var poll = scope.ServiceProvider.GetRequiredService<IPollLock>();
        var hold = await poll.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(hold);
        await using (hold)
        {
            var searches = MockState.SearchAttempts;
            var posts = MockState.TokenPosts;
            var errorsBefore = await CountErrors(factory);
            var sync = scope.ServiceProvider.GetRequiredService<TransferSync>();

            var status = await sync.Run(tenantId, manual: false, CancellationToken.None);
            Assert.Equal(SyncStatus.ReconnectRequired, status);
            var again = await sync.Run(tenantId, manual: false, CancellationToken.None);
            Assert.Equal(SyncStatus.ReconnectRequired, again);

            Assert.Equal(searches, MockState.SearchAttempts);
            Assert.Equal(posts, MockState.TokenPosts);
            Assert.Equal(errorsBefore, await CountErrors(factory));
            var ids = await scope.ServiceProvider.GetRequiredService<IMerchantStore>().ListConnectedTenantIdsAsync(CancellationToken.None);
            Assert.DoesNotContain(tenantId, ids);

            var stored = await LoadConnection(factory, cookie);
            Assert.True(stored.ReconnectRequired);
            var merchant = await LoadMerchant(factory, cookie);
            Assert.Null(merchant.RefreshNotBefore);
            Assert.Null(merchant.RateLimitedUntil);
        }
    }

    private static async Task Expire(TransfersApiFactory factory, string cookie, DateTimeOffset expiresAt)
    {
        var tenantId = await TenantId(factory, cookie);
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
        var connection = await db.Connections.SingleAsync();
        connection.AccessTokenExpiresAt = expiresAt;
        await db.SaveChangesAsync();
    }

    private static async Task<MerchantConnection> LoadConnection(TransfersApiFactory factory, string cookie)
    {
        var tenantId = await TenantId(factory, cookie);
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
        return await db.Connections.AsNoTracking().SingleAsync();
    }

    private static async Task<Merchant> LoadMerchant(TransfersApiFactory factory, string cookie)
    {
        var tenantId = await TenantId(factory, cookie);
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
        return await db.Merchants.AsNoTracking().SingleAsync();
    }

    private static async Task<Guid> TenantId(TransfersApiFactory factory, string cookie)
    {
        using var client = factory.CreateClient();
        using var session = await Send(client, "/api/session", cookie, HttpMethod.Get);
        session.EnsureSuccessStatusCode();
        return (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
    }

    private static async Task<(string Cookie, string Email)> ConnectOwner(HttpClient client, TransfersApiFactory factory)
    {
        var email = $"mp-auth-{Guid.NewGuid():N}@counter.test";
        using var signup = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        signup.EnsureSuccessStatusCode();
        var cookie = signup.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var sessionClient = factory.CreateClient();
        using var session = await Send(sessionClient, "/api/session", cookie, HttpMethod.Get);
        var tenantId = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var merchant = await store.FindMerchantAsync(tenantId, CancellationToken.None);
        Assert.NotNull(merchant);
        merchant!.PlanCode = PlanCodes.PackMes;
        await store.SaveChangesAsync(CancellationToken.None);

        using var started = await Send(client, "/api/connections/mercadopago", cookie);
        var authorizationUrl = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var merchantLogin = await browser.GetAsync(authorizationUrl);
        var query = merchantLogin.Headers.Location!.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        using var callback = await Send(client, $"/api/connections/mercadopago/callback?code={query["code"]}&state={query["state"]}", cookie, HttpMethod.Get);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return (cookie, email);
    }

    private static async Task<string> OwnerCookie(HttpClient client)
    {
        using var signIn = await client.PostAsJsonAsync("/api/owner/sessions", new { email = "owner@transferscheck.test", password = "owner-password-1" });
        signIn.EnsureSuccessStatusCode();
        return signIn.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_owner=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<int> CountErrors(TransfersApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
        return await db.ErrorLogs.CountAsync();
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return client.SendAsync(request);
    }
}
