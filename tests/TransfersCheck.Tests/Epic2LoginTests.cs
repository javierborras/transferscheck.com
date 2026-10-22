using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic2LoginTests(SqlFixture sql)
{
    [Fact]
    public async Task Sign_up_sets_a_cross_site_cookie_and_ignores_a_client_tenant()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var supplied = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            email = $"owner-{Guid.NewGuid():N}@counter.test",
            password = "password1",
            tenantId = supplied
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=None", setCookie, StringComparison.OrdinalIgnoreCase);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(setCookie.Split(';')[0].Split('=', 2)[1]);
        var tenantId = Guid.Parse(token.Claims.Single(claim => claim.Type == "tid").Value);
        Assert.NotEqual(supplied, tenantId);

        using var session = await Send(client, HttpMethod.Get, "/api/session", CookiePair(setCookie));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var body = await session.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenantId, body.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task A_merchant_stops_at_five_login_accounts()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var ownerCookie = await SignUp(client, $"cap-{Guid.NewGuid():N}@counter.test");
        for (var i = 0; i < 4; i++)
        {
            using var created = await Send(client, HttpMethod.Post, "/api/logins", ownerCookie, new
            {
                email = $"till-{i}-{Guid.NewGuid():N}@counter.test",
                password = "password1"
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var refused = await Send(client, HttpMethod.Post, "/api/logins", ownerCookie, new
        {
            email = $"till-extra-{Guid.NewGuid():N}@counter.test",
            password = "password1"
        });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_new_login_replaces_the_previous_session()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var email = $"one-{Guid.NewGuid():N}@counter.test";
        var first = await SignUp(client, email);
        using var secondResponse = await client.PostAsJsonAsync("/api/sessions", new { email, password = "password1" });
        var second = CookiePair(secondResponse.Headers.GetValues("Set-Cookie").Single());

        using var oldSession = await Send(client, HttpMethod.Get, "/api/session", first);
        using var newSession = await Send(client, HttpMethod.Get, "/api/session", second);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newSession.StatusCode);
    }

    [Fact]
    public async Task Revoke_ends_the_session_and_deletes_the_push_token()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var ownerCookie = await SignUp(client, $"revoke-owner-{Guid.NewGuid():N}@counter.test");
        var counterEmail = $"revoke-till-{Guid.NewGuid():N}@counter.test";
        using var created = await Send(client, HttpMethod.Post, "/api/logins", ownerCookie, new { email = counterEmail, password = "password1" });
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var accountId = createdBody.GetProperty("id").GetGuid();

        using var counterSignIn = await client.PostAsJsonAsync("/api/sessions", new { email = counterEmail, password = "password1" });
        var counterCookie = CookiePair(counterSignIn.Headers.GetValues("Set-Cookie").Single());
        using var counterSession = await Send(client, HttpMethod.Get, "/api/session", counterCookie);
        var sessionBody = await counterSession.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = sessionBody.GetProperty("tenantId").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.TenantId = tenantId;
            var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
            await store.AddPushDeviceAsync(new PushDevice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AccountId = accountId,
                Token = "device-token"
            }, CancellationToken.None);
        }

        using var revoked = await Send(client, HttpMethod.Delete, $"/api/logins/{accountId}", ownerCookie);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var after = await Send(client, HttpMethod.Get, "/api/session", counterCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        using var scopeAfter = factory.Services.CreateScope();
        var tenantAfter = scopeAfter.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantAfter.TenantId = tenantId;
        var storeAfter = scopeAfter.ServiceProvider.GetRequiredService<IMerchantStore>();
        Assert.False(await storeAfter.HasPushDeviceAsync(CancellationToken.None));
    }

    private static async Task<string> SignUp(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        response.EnsureSuccessStatusCode();
        return CookiePair(response.Headers.GetValues("Set-Cookie").Single());
    }

    private static string CookiePair(string setCookie) => setCookie.Split(';')[0];

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
