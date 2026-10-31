using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

public sealed class PollingCadenceTests
{
    [Fact]
    public void Activity_reduces_frequency_without_any_commercial_quota()
    {
        var now = DateTimeOffset.UtcNow; var options = new SyncOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), PollingCadence.Interval(now, now, options));
        Assert.Equal(TimeSpan.FromSeconds(60), PollingCadence.Interval(now.AddMinutes(-3), now, options));
        Assert.Equal(TimeSpan.FromMinutes(5), PollingCadence.Interval(now.AddMinutes(-11), now, options));
        Assert.Equal(TimeSpan.FromMinutes(5), PollingCadence.Interval(null, now, options));
        Assert.Equal(TimeSpan.FromSeconds(10), options.ButtonCooldown);
        Assert.Null(typeof(CommercialOptions).GetProperty("WeeklyQueryCap"));
    }
}

[Collection("sql")]
public sealed class TechnicalRefreshTests(SqlFixture sql)
{
    [Fact]
    public async Task Repeated_refresh_is_throttled_for_ten_seconds_without_a_usage_balance()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var signup = await client.PostAsJsonAsync("/api/accounts", new { email = $"quota-{Guid.NewGuid():N}@counter.test", password = "password1" });
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

        using var first = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var searches = MockState.SearchCount;
        using var blocked = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(searches, MockState.SearchCount);
        Assert.Equal(10, (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("retryAfterSeconds").GetInt32());
        clock.Now = clock.Now.AddSeconds(10);
        using var later = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        using var billing = await Send(client, "/api/billing", cookie, HttpMethod.Get);
        var state = await billing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(state.TryGetProperty("consultasCap", out _));
        Assert.False(state.TryGetProperty("consultasUsed", out _));
    }
    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url); request.Headers.TryAddWithoutValidation("Cookie", cookie); return client.SendAsync(request);
    }
}
