using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransfersCheck.Api.Configuration;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class ObservabilityTests(SqlFixture sql)
{
    [Fact]
    public async Task Correlation_id_is_echoed_on_the_response()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var id = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/plans");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", id);
        using var echoed = await client.SendAsync(request);
        Assert.True(echoed.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Equal(id, Assert.Single(values));

        using var generated = await client.GetAsync("/api/plans");
        Assert.True(generated.Headers.TryGetValues("X-Correlation-ID", out var created));
        var assigned = Assert.Single(created);
        Assert.True(Correlation.Accept(assigned));
        Assert.NotEqual(id, assigned);
    }

    [Fact]
    public async Task Error_log_row_is_written_without_a_payment_field()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var marker = "row" + Guid.NewGuid().ToString("N");
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TransfersCheck.Checkout");
        logger.LogError("Checkout failed {Marker} payment={payment}", marker, "card_number=4111111111111111");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<TransfersCheckDb>().ErrorLogs
                .SingleAsync(item => item.Message.Contains(marker));
            Assert.Equal("Error", row.Level);
            Assert.DoesNotContain("4111", row.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("card_number", row.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payment", row.Message, StringComparison.OrdinalIgnoreCase);
        }

        var owner = await OwnerCookie(client);
        using var list = await Send(client, "/api/owner/logs", owner);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var json = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("4111", json, StringComparison.Ordinal);
        Assert.DoesNotContain("card_number", json, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        var shown = doc.RootElement.EnumerateArray().Single(item => item.GetProperty("message").GetString()!.Contains(marker));
        var names = shown.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["occurredAt", "correlationId", "message", "path", "level"], names);
        Assert.Equal("Error", shown.GetProperty("level").GetString());
    }

    [Fact]
    public async Task Commerce_session_cannot_read_the_owner_log_list()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var cookie = await SignUp(client, $"logs-{Guid.NewGuid():N}@counter.test");

        using var anon = await client.GetAsync("/api/owner/logs");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        using var denied = await Send(client, "/api/owner/logs", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("correlationId", await denied.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SignUp(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_session=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<string> OwnerCookie(HttpClient client)
    {
        using var signIn = await client.PostAsJsonAsync("/api/owner/sessions", new { email = "owner@transferscheck.test", password = "owner-password-1" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        return signIn.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_owner=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return await client.SendAsync(request);
    }
}
