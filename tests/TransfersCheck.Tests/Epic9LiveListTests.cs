using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.MockServer;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic9LiveListTests(SqlFixture sql)
{
    [Fact]
    public async Task Shared_live_transfer_contract_is_consumed_by_api_and_web_without_naming_signalr()
    {
        var fixture = File.ReadAllText(Path.Combine(RepoRoot(), "contracts", LiveTransferArrived.FixtureFileName));
        LiveTransferArrived.AssertMatchesFixture(fixture);
        Assert.Equal("LiveTransferArrived", LiveTransferArrived.MessageType);

        var webContract = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "liveTransferContract.ts"));
        Assert.Contains(LiveTransferArrived.MessageType, webContract, StringComparison.Ordinal);
        Assert.Contains("payerName", webContract, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalR", webContract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hub", webContract, StringComparison.Ordinal);

        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("liveTransferMessageType", app, StringComparison.Ordinal);
        Assert.Contains("/api/transfers/live", app, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalR", app, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TransferPollWorker", File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Api", "Program.cs")), StringComparison.Ordinal);

        MockState.Reset();
        MockState.UserId = Random.Shared.Next(2000, 2_000_000_000);
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await SignUpAndConnect(client);

        using var liveRequest = new HttpRequestMessage(HttpMethod.Get, "/api/transfers/live");
        liveRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var liveResponse = await client.SendAsync(liveRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Contains("text/event-stream", liveResponse.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);

        using var refresh = await Send(client, "/api/transfers/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        await using var stream = await liveResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var deadline = DateTime.UtcNow.AddSeconds(8);
        string? dataLine = null;
        while (DateTime.UtcNow < deadline && dataLine is null)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                dataLine = line["data: ".Length..];
        }
        Assert.False(string.IsNullOrEmpty(dataLine));
        using var doc = JsonDocument.Parse(dataLine!);
        Assert.Equal(LiveTransferArrived.MessageType, doc.RootElement.GetProperty("messageType").GetString());
        Assert.True(doc.RootElement.TryGetProperty("id", out _));
        Assert.True(doc.RootElement.TryGetProperty("payerName", out _));
        Assert.True(doc.RootElement.TryGetProperty("amount", out _));
        Assert.True(doc.RootElement.TryGetProperty("occurredAt", out _));
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
        Assert.True(doc.RootElement.TryGetProperty("statusLabel", out _));
    }

    private static async Task<string> SignUpAndConnect(HttpClient client)
    {
        using var signup = await client.PostAsJsonAsync("/api/accounts", new { email = $"live-{Guid.NewGuid():N}@counter.test", password = "password1" });
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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
