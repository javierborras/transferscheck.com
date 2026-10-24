using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic6PushTests(SqlFixture sql)
{
    [Fact]
    public async Task Push_reaches_only_an_approved_account_and_only_when_it_arrived()
    {
        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        var keyPath = WriteServiceAccount();
        var clock = new MutableClock();
        await using var factory = new TransfersApiFactory(sql) { Clock = clock, ServiceAccountPath = keyPath };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var owner = await ConnectOwner(client);
        using var plan = await Send(client, "/api/dev/plan-code", owner, HttpMethod.Put, new { planCode = "pack_mes" });
        Assert.Equal(HttpStatusCode.NoContent, plan.StatusCode);
        var staffEmail = $"staff-{Guid.NewGuid():N}@counter.test";
        using var created = await Send(client, "/api/logins", owner, json: new { email = staffEmail, password = "password1" });
        var staffId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var approved = await Send(client, $"/api/logins/{staffId}/push", owner, HttpMethod.Put, new { approved = true });
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);

        using var staffSignIn = await client.PostAsJsonAsync("/api/sessions", new { email = staffEmail, password = "password1" });
        var staff = staffSignIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var staffToken = await Send(client, "/api/push/tokens", staff, json: new { token = "device-approved" });
        Assert.Equal(HttpStatusCode.NoContent, staffToken.StatusCode);
        using var ownerToken = await Send(client, "/api/push/tokens", owner, json: new { token = "device-owner" });
        Assert.Equal(HttpStatusCode.NoContent, ownerToken.StatusCode);

        using var checklist = await Send(client, "/api/checklist", owner, HttpMethod.Get);
        var alertsDone = (await checklist.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alertsDone").GetBoolean();
        Assert.True(alertsDone);

        MockState.SentPushes.Clear();
        using var synced = await Send(client, "/api/transfers/refresh", owner);
        Assert.Equal(HttpStatusCode.OK, synced.StatusCode);
        Assert.Equal(8, MockState.SentPushes.Count);
        Assert.All(MockState.SentPushes, body =>
        {
            using var doc = JsonDocument.Parse(body);
            var message = doc.RootElement.GetProperty("message");
            Assert.Equal("device-approved", message.GetProperty("token").GetString());
            Assert.Equal("Llegó", message.GetProperty("notification").GetProperty("title").GetString());
            Assert.DoesNotContain("Pendiente Uno", message.GetProperty("notification").GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Rechazado Dos", message.GetProperty("notification").GetProperty("body").GetString(), StringComparison.Ordinal);
        });
        Assert.Equal("Bearer ya29.mock", MockState.LastPushAuthorization);

        using var list = await Send(client, "/api/transfers", owner, HttpMethod.Get);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
        var last = items[^1].GetProperty("id").GetGuid();
        using var focused = await Send(client, $"/api/transfers?first={last}", owner, HttpMethod.Get);
        var first = (await focused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(last, first);

        clock.Now = clock.Now.AddSeconds(61);
        MockState.Payments.RemoveAll(payment => payment.GetProperty("id").GetInt32() == 9);
        MockState.Payments.Add(MockState.Payment(9, "approved", "account_money", 50m, "Pendiente Uno"));
        using var changed = await Send(client, "/api/transfers/refresh", owner);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(9, MockState.SentPushes.Count);
        using (var arrived = JsonDocument.Parse(MockState.SentPushes[^1]))
        {
            var message = arrived.RootElement.GetProperty("message");
            Assert.Contains("Pendiente Uno", message.GetProperty("notification").GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal("Llegó", message.GetProperty("notification").GetProperty("title").GetString());
        }
    }

    [Fact]
    public void The_pwa_is_installable_from_the_ui_origin()
    {
        var root = RepoRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "web", "public", "manifest.webmanifest"));
        var worker = File.ReadAllText(Path.Combine(root, "web", "public", "sw.js"));
        var index = File.ReadAllText(Path.Combine(root, "web", "index.html"));
        var docker = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        Assert.Contains("\"display\": \"standalone\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"scope\": \"/\"", manifest, StringComparison.Ordinal);
        Assert.Contains("notificationclick", worker, StringComparison.Ordinal);
        Assert.Contains("/?first=", worker, StringComparison.Ordinal);
        Assert.Contains("manifest.webmanifest", index, StringComparison.Ordinal);
        Assert.DoesNotContain("web/", docker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_service_account_json_is_not_in_the_repo()
    {
        var root = RepoRoot();
        var hits = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("private_key", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(hits);
    }

    private static string WriteServiceAccount()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.Combine(Path.GetTempPath(), "transferscheck-e6-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            client_email = "mock@transferscheck.test",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = "http://127.0.0.1:5099/google/token"
        }));
        return path;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static async Task<string> ConnectOwner(HttpClient client)
    {
        using var signup = await client.PostAsJsonAsync("/api/accounts", new
        {
            email = $"push-{Guid.NewGuid():N}@counter.test",
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

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null, object? json = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (json is not null)
            request.Content = JsonContent.Create(json);
        return client.SendAsync(request);
    }
}
