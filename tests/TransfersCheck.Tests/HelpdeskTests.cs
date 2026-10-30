using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class HelpdeskTests(SqlFixture sql)
{
    [Fact]
    public async Task Latch_counts_one_row_per_fingerprint_and_keeps_the_links()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        var fingerprint = $"fp-{Guid.NewGuid():N}";
        var other = $"fp-{Guid.NewGuid():N}";
        const string pull = "https://github.com/javierborras/transferscheck.com/pull/9";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            await HelpdeskStore.TouchAsync(db, fingerprint, "api-20261027.1", "sentry-1", 42, pull, CancellationToken.None);
            await HelpdeskStore.TouchAsync(db, fingerprint, "api-20261027.2", null, null, null, CancellationToken.None);
            await HelpdeskStore.TouchAsync(db, other, "api-20261027.2", "https://example.sentry.io/issues/7", null, null, CancellationToken.None);

            var rows = await HelpdeskStore.ListIncidentsAsync(db, CancellationToken.None);
            var row = Assert.Single(rows, item => item.Fingerprint == fingerprint);
            Assert.Equal(2, row.Count);
            Assert.Equal("api-20261027.2", row.ReleaseTag);
            Assert.Equal("sentry-1", row.SentryId);
            Assert.Null(row.SentryUrl);
            Assert.Equal(42, row.GitHubIssueNumber);
            Assert.Equal("https://github.com/javierborras/transferscheck.com/issues/42", row.IssueUrl);
            Assert.Equal(pull, row.PullRequestUrl);
            var linked = Assert.Single(rows, item => item.Fingerprint == other);
            Assert.Equal(1, linked.Count);
            Assert.Equal("https://example.sentry.io/issues/7", linked.SentryUrl);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await OwnerCookie(client);
        using var list = await Send(client, "/api/owner/helpdesk/incidents", cookie);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var shown = doc.RootElement.EnumerateArray().Single(item => item.GetProperty("fingerprint").GetString() == fingerprint);
        Assert.Equal(2, shown.GetProperty("count").GetInt32());
        Assert.Equal("api-20261027.2", shown.GetProperty("releaseTag").GetString());
        Assert.DoesNotContain(shown.EnumerateObject(), property => property.Name is "stack" or "stackTrace" or "exception");
    }

    [Fact]
    public async Task Commerce_message_is_listed_and_closed_without_opening_an_incident()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var email = $"helpdesk-{Guid.NewGuid():N}@counter.test";
        var alias = Guid.NewGuid().ToString("N")[..12];
        var text = $"Consulta {Guid.NewGuid():N}";
        var cookie = await SignUp(client, email);

        using var session = await Send(client, "/api/session", cookie);
        var tenantId = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.TenantId = tenantId;
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            var merchant = await db.Merchants.SingleAsync();
            merchant.Alias = alias;
            await db.SaveChangesAsync();
        }

        int latchesBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
            latchesBefore = await scope.ServiceProvider.GetRequiredService<TransfersCheckDb>().IncidentLatches.CountAsync();

        using var created = await Send(client, "/api/help/messages", cookie, HttpMethod.Post, new { text, tenantId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdJson = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("issue", createdJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pull", createdJson, StringComparison.OrdinalIgnoreCase);
        using var createdDoc = JsonDocument.Parse(createdJson);
        var id = createdDoc.RootElement.GetProperty("id").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            Assert.Equal(latchesBefore, await db.IncidentLatches.CountAsync());
            var stored = await db.CommerceHelpMessages.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
            Assert.Equal(tenantId, stored.TenantId);
            Assert.Equal(email, stored.AdminEmail);
        }

        var owner = await OwnerCookie(client);
        using var list = await Send(client, "/api/owner/helpdesk/messages", owner);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var message = doc.RootElement.EnumerateArray().Single(item => item.GetProperty("text").GetString() == text);
        Assert.Equal(alias, message.GetProperty("alias").GetString());
        Assert.Equal(email, message.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, message.GetProperty("closedAt").ValueKind);
        Assert.DoesNotContain(message.EnumerateObject(), property => property.Name is "cuit" or "address" or "balance" or "saldo" or "stack" or "stackTrace");

        using var closed = await Send(client, $"/api/owner/helpdesk/messages/{id}/close", owner, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var closedAt = (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("closedAt").GetString();
        Assert.False(string.IsNullOrWhiteSpace(closedAt));

        using var again = await Send(client, $"/api/owner/helpdesk/messages/{id}/close", owner, HttpMethod.Post);
        Assert.Equal(closedAt, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("closedAt").GetString());
    }

    [Fact]
    public async Task Commerce_session_cannot_read_the_owner_helpdesk()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var cookie = await SignUp(client, $"denied-{Guid.NewGuid():N}@counter.test");

        using var anon = await client.GetAsync("/api/owner/helpdesk/incidents");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        using var incidents = await Send(client, "/api/owner/helpdesk/incidents", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, incidents.StatusCode);
        using var messages = await Send(client, "/api/owner/helpdesk/messages", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, messages.StatusCode);
        using var close = await Send(client, $"/api/owner/helpdesk/messages/{Guid.NewGuid()}/close", cookie, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.Unauthorized, close.StatusCode);

        using var unsigned = await client.PostAsJsonAsync("/api/help/messages", new { text = "sin sesion" });
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
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

    private static async Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null, object? json = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (json is not null)
            request.Content = JsonContent.Create(json);
        return await client.SendAsync(request);
    }
}
