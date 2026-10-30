using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TransfersCheck.Api.Owner;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class OwnerDashboardTests(SqlFixture sql)
{
    [Fact]
    public async Task Owner_reads_require_the_owner_cookie()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var anon = await client.GetAsync("/api/owner/home");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using var activity = await client.GetAsync("/api/owner/activity");
        Assert.Equal(HttpStatusCode.Unauthorized, activity.StatusCode);
        using var devices = await client.GetAsync("/api/owner/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, devices.StatusCode);
        using var edit = await client.PutAsJsonAsync($"/api/owner/clients/{Guid.NewGuid()}", new { alias = "nada", planCode = "pack_mes" });
        Assert.Equal(HttpStatusCode.Unauthorized, edit.StatusCode);

        using var signup = await client.PostAsJsonAsync("/api/accounts", new { email = $"dash-{Guid.NewGuid():N}@counter.test", password = "password1" });
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var commerceCookie = signup.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_session=", StringComparison.Ordinal)).Split(';')[0];
        using var denied = await Send(client, "/api/owner/clients", commerceCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Owner_lists_clients_and_filters_billing_without_commerce_transfers()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await OwnerCookie(client);

        var linkedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var linkedEmail = $"linked-{Guid.NewGuid():N}@counter.test";
        var otherEmail = $"other-{Guid.NewGuid():N}@counter.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            db.Merchants.Add(Merchant(linkedId, Guid.NewGuid().ToString("N")[..16]));
            db.Merchants.Add(Merchant(otherId, Guid.NewGuid().ToString("N")[..16]));
            db.LoginAccounts.Add(Admin(linkedId, linkedEmail));
            db.LoginAccounts.Add(Admin(otherId, otherEmail));
            db.Connections.Add(new MerchantConnection
            {
                Id = Guid.NewGuid(),
                TenantId = linkedId,
                Source = "mercadopago",
                ExternalAccountId = Guid.NewGuid().ToString("N"),
                AccessTokenProtected = "x",
                RefreshTokenProtected = "x",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            db.Transfers.Add(new Transfer
            {
                Id = Guid.NewGuid(),
                TenantId = linkedId,
                Source = "mercadopago",
                ExternalId = Guid.NewGuid().ToString("N"),
                PayerName = "PagadorOcultoDash",
                Amount = 424242.42m,
                Currency = "ARS",
                OccurredAt = DateTimeOffset.UtcNow,
                Status = "approved",
                TypeCode = "bank_transfer"
            });
            db.BillingOrders.Add(Order(linkedId, 424201, paid: true));
            db.BillingOrders.Add(Order(otherId, 424202, paid: false));
            await db.SaveChangesAsync();
        }

        using var clients = await Send(client, "/api/owner/clients", cookie);
        Assert.Equal(HttpStatusCode.OK, clients.StatusCode);
        var clientJson = await clients.Content.ReadAsStringAsync();
        using var clientDoc = JsonDocument.Parse(clientJson);
        var linked = clientDoc.RootElement.EnumerateArray().Single(row => row.GetProperty("email").GetString() == linkedEmail);
        var other = clientDoc.RootElement.EnumerateArray().Single(row => row.GetProperty("email").GetString() == otherEmail);
        Assert.True(linked.GetProperty("connected").GetBoolean());
        Assert.False(other.GetProperty("connected").GetBoolean());
        Assert.Equal(1, linked.GetProperty("paidOrdersCount").GetInt32());
        Assert.Equal(424201m, linked.GetProperty("totalPaidAmount").GetDecimal());
        Assert.Equal(0, other.GetProperty("paidOrdersCount").GetInt32());
        Assert.DoesNotContain("PagadorOcultoDash", clientJson, StringComparison.Ordinal);
        Assert.DoesNotContain("424242", clientJson, StringComparison.Ordinal);
        Assert.DoesNotContain("externalAccountId", clientJson, StringComparison.OrdinalIgnoreCase);

        using var all = await Send(client, "/api/owner/billing", cookie);
        var allJson = await all.Content.ReadAsStringAsync();
        Assert.Contains("424201", allJson, StringComparison.Ordinal);
        Assert.Contains("424202", allJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PagadorOcultoDash", allJson, StringComparison.Ordinal);
        using var allDoc = JsonDocument.Parse(allJson);
        var paid = allDoc.RootElement.EnumerateArray().Single(row => row.GetProperty("amount").GetInt32() == 424201);
        Assert.Equal(linkedEmail, paid.GetProperty("email").GetString());
        Assert.NotEqual(JsonValueKind.Null, paid.GetProperty("paidAt").ValueKind);

        using var filtered = await Send(client, $"/api/owner/billing?tenantId={linkedId}", cookie);
        var filteredDoc = await filtered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(filteredDoc.EnumerateArray(), row => row.GetProperty("amount").GetInt32() == 424201);
        Assert.DoesNotContain(filteredDoc.EnumerateArray(), row => row.GetProperty("amount").GetInt32() == 424202);
    }

    [Fact]
    public async Task Home_reports_trial_rate_limit_and_missing_mail_and_webhook()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await OwnerCookie(client);

        using var beforeMessage = await Send(client, "/api/owner/home", cookie);
        var before = await beforeMessage.Content.ReadFromJsonAsync<JsonElement>();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            var id = Guid.NewGuid();
            var merchant = Merchant(id, null);
            merchant.PlanCode = PlanCodes.PackMes;
            merchant.TrialEndsAt = DateTimeOffset.UtcNow.AddDays(3);
            merchant.RateLimitedUntil = DateTimeOffset.UtcNow.AddHours(1);
            db.Merchants.Add(merchant);
            db.LoginAccounts.Add(Admin(id, $"trial-{Guid.NewGuid():N}@counter.test"));
            await db.SaveChangesAsync();
        }

        using var afterMessage = await Send(client, "/api/owner/home", cookie);
        var after = await afterMessage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Count(before, "tasks", "trials_ending") + 1, Count(after, "tasks", "trials_ending"));
        Assert.Equal(Count(before, "health", "rate_limited") + 1, Count(after, "health", "rate_limited"));
        Assert.Contains(after.GetProperty("tasks").EnumerateArray(), task => task.GetProperty("code").GetString() == "mail_unset");
        Assert.Contains(after.GetProperty("tasks").EnumerateArray(), task => task.GetProperty("code").GetString() == "webhook_unsigned");
        Assert.DoesNotContain(after.GetProperty("tasks").EnumerateArray(), task => task.GetProperty("code").GetString() == "checkout_token_unset");
    }

    [Fact]
    public async Task Swagger_lists_owner_dashboard_routes_and_not_a_transfer_read()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/owner/home", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/clients", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/billing", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/activity", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/devices", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/movements", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/plans", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/services", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/sessions/ticket", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/helpdesk/incidents", json, StringComparison.Ordinal);
        Assert.Contains("/api/owner/helpdesk/messages", json, StringComparison.Ordinal);
        Assert.Contains("/api/help/messages", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/owner/transfers", json, StringComparison.Ordinal);
    }

    private static int Count(JsonElement root, string group, string code)
    {
        var valueName = group == "tasks" ? "count" : "value";
        foreach (var item in root.GetProperty(group).EnumerateArray())
        {
            if (item.GetProperty("code").GetString() == code)
                return item.GetProperty(valueName).GetInt32();
        }
        return 0;
    }

    private static Merchant Merchant(Guid id, string? alias, DateTimeOffset? createdAt = null) => new()
    {
        Id = id,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        Alias = alias,
        ChecklistStep = "account"
    };

    private static Merchant Ready(Guid id, string alias, DateTimeOffset? accessUntil, DateTimeOffset? lastCheck) => new()
    {
        Id = id,
        CreatedAt = DateTimeOffset.UtcNow,
        Alias = alias,
        ChecklistStep = "account",
        PlanCode = PlanCodes.PackMes,
        AccessUntil = accessUntil,
        LastTransferCheckAt = lastCheck
    };

    private static MerchantConnection Connection(Guid tenantId, bool reconnect) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Source = "mercadopago",
        ExternalAccountId = Guid.NewGuid().ToString("N"),
        AccessTokenProtected = "x",
        RefreshTokenProtected = "x",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        ReconnectRequired = reconnect
    };

    private static async Task<JsonElement> One(HttpClient client, string cookie, string email)
    {
        using var message = await Send(client, $"/api/owner/clients?q={Uri.EscapeDataString(email)}", cookie);
        var doc = await message.Content.ReadFromJsonAsync<JsonElement>();
        return doc.EnumerateArray().Single(row => row.GetProperty("email").GetString() == email);
    }

    private static async Task AssertFilter(HttpClient client, string cookie, string query, string[] present, string[] absent)
    {
        using var message = await Send(client, $"/api/owner/clients?{query}", cookie);
        Assert.Equal(HttpStatusCode.OK, message.StatusCode);
        var doc = await message.Content.ReadFromJsonAsync<JsonElement>();
        var emails = doc.EnumerateArray().Select(row => row.GetProperty("email").GetString()).ToArray();
        foreach (var email in present)
            Assert.Contains(email, emails);
        foreach (var email in absent)
            Assert.DoesNotContain(email, emails);
    }

    private static JsonElement Point(JsonElement root, string date)
    {
        foreach (var point in root.GetProperty("points").EnumerateArray())
        {
            var raw = point.GetProperty("date");
            var value = raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.ToString();
            if (value is not null && value.StartsWith(date, StringComparison.Ordinal))
                return point;
        }

        throw new InvalidOperationException($"Missing {date}.");
    }

    private static LoginAccount Admin(Guid tenantId, string email) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email,
        PasswordHash = "x",
        IsOwner = true,
        PasswordSet = true
    };

    private static BillingOrder Order(Guid tenantId, int amount, bool paid) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PlanCode = PlanCodes.PackMes,
        Amount = amount,
        Currency = "ARS",
        CreatedAt = DateTimeOffset.UtcNow,
        PaidAt = paid ? DateTimeOffset.UtcNow : null
    };

    [Fact]
    public async Task Activity_counts_signups_by_created_at_and_queries_by_recorded_calls()
    {
        var now = new DateTimeOffset(2026, 10, 29, 18, 0, 0, TimeSpan.Zero);
        await using var factory = new TransfersApiFactory(sql) { Owner = true, Clock = new MutableClock { Now = now } };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await OwnerCookie(client);
        var recentId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            db.Merchants.Add(Merchant(recentId, null, now.AddDays(-2)));
            db.Merchants.Add(Merchant(oldId, null, now.AddDays(-40)));
            db.Merchants.Add(Merchant(otherId, null, now.AddDays(-1)));
            db.MercadoPagoApiCalls.Add(new MercadoPagoApiCall { OccurredAt = now, TenantId = recentId });
            db.MercadoPagoApiCalls.Add(new MercadoPagoApiCall { OccurredAt = now.AddDays(-1), TenantId = otherId });
            db.MercadoPagoApiCalls.Add(new MercadoPagoApiCall { OccurredAt = now.AddDays(-40), TenantId = oldId });
            await db.SaveChangesAsync();
        }

        using var recentMessage = await Send(client, $"/api/owner/activity?days=7&tenantId={recentId}", cookie);
        Assert.Equal(HttpStatusCode.OK, recentMessage.StatusCode);
        var recent = await recentMessage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(recent.GetProperty("showSignups").GetBoolean());
        Assert.Equal(7, recent.GetProperty("points").GetArrayLength());
        Assert.Equal(1, Point(recent, "2026-10-27").GetProperty("signups").GetInt32());
        Assert.Equal(0, Point(recent, "2026-10-27").GetProperty("queries").GetInt32());
        Assert.Equal(1, Point(recent, "2026-10-29").GetProperty("queries").GetInt32());
        Assert.Equal(0, Point(recent, "2026-10-28").GetProperty("queries").GetInt32());
        Assert.Equal("2026-10-29", recent.GetProperty("callsRecordedFrom").GetString());

        using var oldMessage = await Send(client, $"/api/owner/activity?days=7&tenantId={oldId}", cookie);
        var old = await oldMessage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(old.GetProperty("showSignups").GetBoolean());
        Assert.Equal(0, old.GetProperty("points").EnumerateArray().Sum(point => point.GetProperty("queries").GetInt32()));
        Assert.Equal("2026-09-19", old.GetProperty("callsRecordedFrom").GetString());

        using var bad = await Send(client, "/api/owner/activity?days=14", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var missing = await Send(client, $"/api/owner/activity?days=7&tenantId={Guid.NewGuid()}", cookie);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task MercadoPago_search_records_one_row_per_http_call()
    {
        TransfersCheck.MockServer.MockState.Reset();
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        var tenantId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<MercadoPagoTransferSource>();
        using (MercadoPagoCallScope.Begin(tenantId))
        {
            await source.QueryAsync(new TokenSet("test", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None);
            await source.QueryAsync(new TokenSet("test", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None);
        }

        var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
        var count = await db.MercadoPagoApiCalls.CountAsync(call => call.TenantId == tenantId);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Client_filters_match_the_health_cards_and_alias_can_be_edited()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await OwnerCookie(client);
        var now = DateTimeOffset.UtcNow;
        var linkedId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var waitingId = Guid.NewGuid();
        var reconnectId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var trialId = Guid.NewGuid();
        var linkedEmail = $"linked-{Guid.NewGuid():N}@counter.test";
        var staleEmail = $"stale-{Guid.NewGuid():N}@counter.test";
        var waitingEmail = $"waiting-{Guid.NewGuid():N}@counter.test";
        var reconnectEmail = $"reconnect-{Guid.NewGuid():N}@counter.test";
        var pendingEmail = $"pending-{Guid.NewGuid():N}@counter.test";
        var trialEmail = $"trial-{Guid.NewGuid():N}@counter.test";
        const string deviceToken = "device-token-should-stay-out";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
            db.Merchants.Add(Ready(linkedId, "verdeok", now.AddDays(20), now));
            db.Merchants.Add(Ready(staleId, "staleok", now.AddDays(20), now.AddHours(-2)));
            var waiting = Ready(waitingId, "esperaok", null, null);
            waiting.RateLimitedUntil = now.AddHours(2);
            db.Merchants.Add(waiting);
            var reconnectMerchant = Ready(reconnectId, "reok1234", now.AddHours(-2), null);
            db.Merchants.Add(reconnectMerchant);
            db.Merchants.Add(Ready(pendingId, "pendok12", now.AddDays(20), now));
            var trialMerchant = Ready(trialId, "trialok1", now.AddDays(20), now);
            trialMerchant.PlanCode = PlanCodes.PackMes;
            trialMerchant.TrialEndsAt = now.AddDays(2);
            db.Merchants.Add(trialMerchant);
            db.LoginAccounts.Add(Admin(linkedId, linkedEmail));
            db.LoginAccounts.Add(Admin(staleId, staleEmail));
            db.LoginAccounts.Add(Admin(waitingId, waitingEmail));
            db.LoginAccounts.Add(Admin(reconnectId, reconnectEmail));
            db.LoginAccounts.Add(Admin(pendingId, pendingEmail));
            db.LoginAccounts.Add(Admin(trialId, trialEmail));
            db.Connections.Add(Connection(linkedId, false));
            db.Connections.Add(Connection(staleId, false));
            db.Connections.Add(Connection(reconnectId, true));
            db.Connections.Add(Connection(pendingId, false));
            db.Connections.Add(Connection(trialId, false));
            db.BillingOrders.Add(Order(pendingId, 880011, paid: false));
            db.PushDevices.Add(new PushDevice { Id = Guid.NewGuid(), TenantId = linkedId, AccountId = db.LoginAccounts.Local.Single(a => a.TenantId == linkedId).Id, Token = deviceToken });
            await db.SaveChangesAsync();
        }

        var linked = await One(client, cookie, linkedEmail);
        Assert.Equal("verde", linked.GetProperty("tone").GetString());
        Assert.Equal("Vinculado", linked.GetProperty("toneLabel").GetString());
        Assert.True(linked.GetProperty("connected").GetBoolean());
        Assert.Equal(1, linked.GetProperty("loginCount").GetInt32());
        Assert.Equal(1, linked.GetProperty("pushDeviceCount").GetInt32());

        var stale = await One(client, cookie, staleEmail);
        Assert.Equal("vinculado", stale.GetProperty("mercadoPago").GetString());
        var waitingRow = await One(client, cookie, waitingEmail);
        Assert.Equal("ambar", waitingRow.GetProperty("tone").GetString());
        Assert.Equal("Sin vincular", waitingRow.GetProperty("toneLabel").GetString());
        var reconnect = await One(client, cookie, reconnectEmail);
        Assert.Equal("rojo", reconnect.GetProperty("tone").GetString());
        Assert.Equal("Hay que volver a vincular", reconnect.GetProperty("toneLabel").GetString());
        Assert.Equal("vencido", reconnect.GetProperty("accessState").GetString());
        var pending = await One(client, cookie, pendingEmail);
        Assert.Equal("Orden pendiente de pago", pending.GetProperty("toneLabel").GetString());
        var trial = await One(client, cookie, trialEmail);
        Assert.Equal("Prueba por vencer", trial.GetProperty("toneLabel").GetString());

        await AssertFilter(client, cookie, "mp=vinculado", present: [linkedEmail, staleEmail], absent: [waitingEmail, reconnectEmail]);
        await AssertFilter(client, cookie, "mp=espera", present: [waitingEmail], absent: [linkedEmail, staleEmail]);
        await AssertFilter(client, cookie, "mp=demoradas", present: [staleEmail], absent: [linkedEmail, waitingEmail]);
        await AssertFilter(client, cookie, "mp=reconectar", present: [reconnectEmail], absent: [linkedEmail]);
        await AssertFilter(client, cookie, "access=vencido", present: [reconnectEmail], absent: [linkedEmail]);
        await AssertFilter(client, cookie, "trial=vigente", present: [trialEmail], absent: [linkedEmail]);
        await AssertFilter(client, cookie, $"q={Uri.EscapeDataString(linkedEmail)}&plan=pack_evento", present: [], absent: [linkedEmail]);

        using var devicesMessage = await Send(client, "/api/owner/devices", cookie);
        var devicesJson = await devicesMessage.Content.ReadAsStringAsync();
        Assert.Contains("verdeok", devicesJson, StringComparison.Ordinal);
        Assert.Contains("Administrador", devicesJson, StringComparison.Ordinal);
        Assert.DoesNotContain(deviceToken, devicesJson, StringComparison.Ordinal);

        using var badPlan = await Send(client, $"/api/owner/clients/{linkedId}", cookie, HttpMethod.Put, new { alias = "verdeok", planCode = "no_such_plan" });
        Assert.Equal(HttpStatusCode.BadRequest, badPlan.StatusCode);
        using var taken = await Send(client, $"/api/owner/clients/{linkedId}", cookie, HttpMethod.Put, new { alias = "staleok", planCode = "pack_mes" });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        using var saved = await Send(client, $"/api/owner/clients/{linkedId}", cookie, HttpMethod.Put, new { alias = "verdeok2", planCode = "pack_evento" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var edited = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("verdeok2", edited.GetProperty("alias").GetString());
        Assert.Equal("pack_evento", edited.GetProperty("planCode").GetString());
        using var still = await Send(client, $"/api/owner/clients/{linkedId}", cookie);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task Inspection_ticket_opens_the_owner_session_without_the_password()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient();
        const string key = "test-signing-key-with-32-bytes-min";
        var ticket = OwnerEndpoints.CreateInspectionTicket("owner@transferscheck.test", key, TimeSpan.FromHours(1));

        using var opened = await client.PostAsJsonAsync("/api/owner/sessions/ticket", new { ticket });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var cookie = opened.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_owner=", StringComparison.Ordinal)).Split(';')[0];
        using var session = await Send(client, "/api/owner/session", cookie);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        using var password = await client.PostAsJsonAsync("/api/owner/sessions/ticket", new { ticket = "owner-password-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, password.StatusCode);

        var longLived = CraftTicket("owner@transferscheck.test", key, DateTimeOffset.UtcNow.AddHours(12));
        using var rejected = await client.PostAsJsonAsync("/api/owner/sessions/ticket", new { ticket = longLived });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    private static string CraftTicket(string email, string signingKey, DateTimeOffset expires)
    {
        var payload = $"{email}|{expires.ToUnixTimeSeconds()}";
        var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(payload)));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));
    }

    private static async Task<string> OwnerCookie(HttpClient client)
    {
        using var signIn = await client.PostAsJsonAsync("/api/owner/sessions", new { email = "owner@transferscheck.test", password = "owner-password-1" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        return signIn.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_owner=", StringComparison.Ordinal)).Split(';')[0];
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie)
        => Send(client, url, cookie, HttpMethod.Get, null);

    private static async Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod method, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
