using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

public sealed class Epic10QuotaPureTests
{
    [Fact]
    public void Weekly_query_quota_warns_at_450_waits_at_500_and_periods_run_seven_days_from_the_link()
    {
        Assert.Equal(QuotaLevel.Warn, WeeklyQueryQuota.Evaluate(450, 500, 50));
        Assert.Equal(QuotaLevel.Ok, WeeklyQueryQuota.Evaluate(449, 500, 50));
        Assert.Equal(QuotaLevel.Wait, WeeklyQueryQuota.Evaluate(500, 500, 50));
        Assert.Equal(QuotaLevel.Wait, WeeklyQueryQuota.Evaluate(501, 500, 50));

        var link = new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);
        var (start, end) = WeeklyQueryQuota.Period(link, link.AddDays(3));
        Assert.Equal(link, start);
        Assert.Equal(link.AddDays(7), end);

        var (secondStart, secondEnd) = WeeklyQueryQuota.Period(link, link.AddDays(7).AddMinutes(1));
        Assert.Equal(link.AddDays(7), secondStart);
        Assert.Equal(link.AddDays(14), secondEnd);
        Assert.Equal(DayOfWeek.Thursday, link.DayOfWeek);

        var policy = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Core", "WeeklyQueryQuota.cs"));
        Assert.Contains("on-demand", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overage", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recharge", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IMerchantStore", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("MediatR", policy, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}

[Collection("sql")]
public sealed class Epic10QuotaTests(SqlFixture sql)
{
    [Fact]
    public async Task On_demand_refresh_increments_consulta_and_waits_at_the_wall()
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
        using var billing = await Send(client, "/api/billing", cookie, HttpMethod.Get);
        Assert.Equal(1, (await billing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("consultasUsed").GetInt32());

        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var session = await Send(client, "/api/session", cookie, HttpMethod.Get);
        tenant.TenantId = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        var counter = scope.ServiceProvider.GetRequiredService<IConsultaCounter>();
        var merchant = await scope.ServiceProvider.GetRequiredService<IMerchantStore>().FindMerchantAsync(tenant.TenantId, CancellationToken.None);
        var (periodStart, _) = WeeklyQueryQuota.Period(merchant!.ConnectedAt!.Value, clock.Now);
        for (var i = 1; i < 500; i++)
            await counter.IncrementOnDemandAsync(tenant.TenantId, clock.Now, periodStart, CancellationToken.None);

        using var wait = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.TooManyRequests, wait.StatusCode);
        Assert.Equal("quota_wait", (await wait.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("consultaWarn", app, StringComparison.Ordinal);
        Assert.Contains("consultas de esta semana", app, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
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
