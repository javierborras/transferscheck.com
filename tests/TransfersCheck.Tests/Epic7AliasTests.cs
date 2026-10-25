using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic7AliasTests(SqlFixture sql)
{
    [Fact]
    public async Task Sign_in_requires_the_email_code_and_refuses_it_after_one_hour()
    {
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient();
        var email = $"code-{Guid.NewGuid():N}@counter.test";
        using var signup = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        signup.EnsureSuccessStatusCode();

        using var withoutCode = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCode.StatusCode);
        Assert.Equal("code_required", (await withoutCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Single(factory.Mail.Sent);
        var code = factory.Mail.Sent[^1].Body.Trim();
        Assert.Equal(6, code.Length);
        Assert.True(code.All(char.IsDigit));

        using var withCode = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1", code });
        Assert.Equal(HttpStatusCode.OK, withCode.StatusCode);

        using var again = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        var lateCode = factory.Mail.Sent[^1].Body.Trim();
        clock.Now = clock.Now.AddHours(1).AddSeconds(1);
        using var expired = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1", code = lateCode });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("invalid_code", (await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("name=\"code\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("6 dígitos", app, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("six digits", app, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Alias_is_confirmed_once_as_hash_alias_and_shown_beside_the_logo()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var cookie = await SignUp(client, $"alias-{Guid.NewGuid():N}@counter.test");
        var alias = $"tienda{Guid.NewGuid():N}"[..12];

        using var confirmed = await Send(client, HttpMethod.Post, "/api/alias", cookie, new { alias });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal($"#{alias}", (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alias").GetString());

        using var again = await Send(client, HttpMethod.Post, "/api/alias", cookie, new { alias = alias + "x" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("alias_locked", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        using var get = await Send(client, HttpMethod.Get, "/api/alias", cookie);
        Assert.Equal($"#{alias}", (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alias").GetString());

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("alias-slot", app, StringComparison.Ordinal);
        Assert.Contains("/api/alias", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pack_mes_invite_is_single_use_one_hour_and_wa_me_only_carries_the_message()
    {
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await SignUp(client, $"invite-{Guid.NewGuid():N}@counter.test");
        await SetPlanCode(factory, cookie, PlanCodes.PackMes);
        var alias = $"mes{Guid.NewGuid():N}"[..10];
        using var aliasOk = await Send(client, HttpMethod.Post, "/api/alias", cookie, new { alias });
        aliasOk.EnsureSuccessStatusCode();

        using var created = await Send(client, HttpMethod.Post, "/api/logins", cookie, new { name = "lucia" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"lucia#{alias}", body.GetProperty("displayName").GetString());
        var inviteUrl = body.GetProperty("inviteUrl").GetString()!;
        var waMe = body.GetProperty("waMeUrl").GetString()!;
        var token = body.GetProperty("token").GetString()!;
        Assert.StartsWith("http://localhost:5183/invitar/", inviteUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("#", inviteUrl.Split('/').Last(), StringComparison.Ordinal);
        Assert.StartsWith("https://wa.me/?text=", waMe, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(inviteUrl), waMe, StringComparison.Ordinal);
        Assert.DoesNotContain("WhatsApp API", File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Api", "appsettings.json")), StringComparison.OrdinalIgnoreCase);

        using var claim = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "password1" });
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var puntoCookie = claim.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var reuse = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "password2" });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        using var signIn = await client.PostAsJsonAsync("/api/sessions", new { commerce = alias, user = "lucia", password = "password1" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        clock.Now = clock.Now.AddHours(1).AddSeconds(1);
        using var lateInvite = await Send(client, HttpMethod.Post, "/api/logins", cookie, new { name = "marco" });
        var lateToken = (await lateInvite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        clock.Now = clock.Now.AddHours(1).AddSeconds(1);
        using var expired = await client.PostAsJsonAsync($"/api/invites/{lateToken}", new { password = "password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("invalid_invite", (await expired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        using var otherBrowser = await client.PostAsJsonAsync("/api/sessions", new { commerce = alias, user = "lucia", password = "password1" });
        otherBrowser.EnsureSuccessStatusCode();
        using var oldSession = await Send(client, HttpMethod.Get, "/api/session", puntoCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("name=\"commerce\"", app, StringComparison.Ordinal);
        Assert.Contains("name=\"user\"", app, StringComparison.Ordinal);
        Assert.Contains("waMeUrl", app, StringComparison.Ordinal);
        Assert.Contains("Enviar por WhatsApp", app, StringComparison.Ordinal);
        Assert.Contains("/invitar/", app, StringComparison.Ordinal);
        _ = puntoCookie;
    }

    [Fact]
    public async Task Punto_de_alerta_sees_alert_point_window_and_detail_without_admin_surfaces()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        await using var factory = new TransfersApiFactory(sql) { PageSize = 20 };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var owner = await SignUp(client, $"ap-owner-{Guid.NewGuid():N}@counter.test");
        await SetPlanCode(factory, owner, PlanCodes.PackMes);
        var alias = $"ap{Guid.NewGuid():N}"[..10];
        using (var confirmed = await Send(client, HttpMethod.Post, "/api/alias", owner, new { alias }))
            confirmed.EnsureSuccessStatusCode();
        using var invite = await Send(client, HttpMethod.Post, "/api/logins", owner, new { name = "vista" });
        var token = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        using var claimed = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "password1" });
        claimed.EnsureSuccessStatusCode();
        var punto = claimed.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var connect = await Send(client, HttpMethod.Post, "/api/connections/mercadopago", owner);
        var authorizationUrl = (await connect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var merchantLogin = await browser.GetAsync(authorizationUrl);
        var query = merchantLogin.Headers.Location!.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        using var callback = await Send(client, HttpMethod.Get, $"/api/connections/mercadopago/callback?code={query["code"]}&state={query["state"]}", owner);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        using var refresh = await Send(client, HttpMethod.Post, "/api/transfers/refresh", owner);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        using var list = await Send(client, HttpMethod.Get, "/api/transfers", punto);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, body.GetProperty("take").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.True(items.Length <= 10);
        Assert.Contains(items, item => item.GetProperty("payerName").GetString() is { Length: > 0 });

        var firstId = items[0].GetProperty("id").GetGuid();
        using var detail = await Send(client, HttpMethod.Get, $"/api/transfers/{firstId}", punto);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(detailBody.TryGetProperty("payerName", out _));
        Assert.True(detailBody.TryGetProperty("amount", out _));
        Assert.True(detailBody.TryGetProperty("statusLabel", out _));

        using var forbiddenRefresh = await Send(client, HttpMethod.Post, "/api/transfers/refresh", punto);
        Assert.Equal(HttpStatusCode.TooManyRequests, forbiddenRefresh.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var session = await Send(client, HttpMethod.Get, "/api/session", owner);
            tenant.TenantId = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
            var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
            var merchant = await store.FindMerchantAsync(tenant.TenantId, CancellationToken.None);
            merchant!.RefreshNotBefore = null;
            await store.SaveChangesAsync(CancellationToken.None);
        }
        using var cashierRefresh = await Send(client, HttpMethod.Post, "/api/transfers/refresh", punto);
        Assert.Equal(HttpStatusCode.OK, cashierRefresh.StatusCode);
        using var usage = await Send(client, HttpMethod.Get, "/api/billing", owner);
        Assert.False((await usage.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("consultasUsed", out _));
        using var forbiddenLogins = await Send(client, HttpMethod.Get, "/api/logins", punto);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenLogins.StatusCode);

        using var historyDenied = await Send(client, HttpMethod.Get, "/api/history", punto);
        Assert.Equal(HttpStatusCode.Forbidden, historyDenied.StatusCode);

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("isOwner", app, StringComparison.Ordinal);
        Assert.Contains("/api/transfers/", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_history_csv_and_totals_refuse_alert_point_and_skip_medio_search()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var owner = await SignUp(client, $"hist-{Guid.NewGuid():N}@counter.test");
        await SetPlanCode(factory, owner, PlanCodes.PackMes);
        using var connect = await Send(client, HttpMethod.Post, "/api/connections/mercadopago", owner);
        var authorizationUrl = (await connect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!;
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var merchantLogin = await browser.GetAsync(authorizationUrl);
        var query = merchantLogin.Headers.Location!.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        using var callback = await Send(client, HttpMethod.Get, $"/api/connections/mercadopago/callback?code={query["code"]}&state={query["state"]}", owner);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        using var refresh = await Send(client, HttpMethod.Post, "/api/transfers/refresh", owner);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        using (var scope = factory.Services.CreateScope()) {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var session = await Send(client, HttpMethod.Get, "/api/session", owner);
            tenant.TenantId = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
            var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
            var merchant = await store.FindMerchantAsync(tenant.TenantId, CancellationToken.None); merchant!.RefreshNotBefore = null; await store.SaveChangesAsync(CancellationToken.None);
        }
        using var history = await Send(client, HttpMethod.Get, "/api/history", owner);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var body = await history.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Movimientos de tu cuenta", body.GetProperty("title").GetString());
        var columns = body.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToArray();
        Assert.Equal(new[] { "Fecha", "Pagador", "Monto", "Estado" }, columns);
        Assert.DoesNotContain("Medio", columns);
        Assert.True(body.GetProperty("items").GetArrayLength() > 10 || body.GetProperty("items").GetArrayLength() >= 1);
        Assert.True(body.GetProperty("totals").TryGetProperty("visible", out _));

        using var csv = await Send(client, HttpMethod.Get, "/api/history/csv", owner);
        Assert.Equal(HttpStatusCode.NotFound, csv.StatusCode);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/api/history/csv", File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx")), StringComparison.Ordinal);

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("Movimientos de tu cuenta", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Medio", app, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"search\"", app, StringComparison.Ordinal);
        Assert.Contains("/api/history", app, StringComparison.Ordinal);
    }

    private static async Task<string> SignUp(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private static async Task SetPlanCode(TransfersApiFactory factory, string cookie, string planCode)
    {
        using var client = factory.CreateClient();
        using var session = await Send(client, HttpMethod.Get, "/api/session", cookie);
        var body = await session.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = body.GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantId;
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var merchant = await store.FindMerchantAsync(tenantId, CancellationToken.None);
        merchant!.PlanCode = planCode;
        await store.SaveChangesAsync(CancellationToken.None);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body is not null)
            request.Content = JsonContent.Create(body);
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
