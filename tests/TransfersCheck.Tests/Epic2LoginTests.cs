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
    public async Task Pack_mes_refuses_a_fifth_punto_de_alerta_and_pack_evento_refuses_a_second_account()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();

        var mesCookie = await SignUp(client, $"mes-{Guid.NewGuid():N}@counter.test");
        await SetPlanCode(factory, mesCookie, PlanCodes.PackMes);
        var mesAlias = $"mes{Guid.NewGuid():N}"[..10];
        using (var alias = await Send(client, HttpMethod.Post, "/api/alias", mesCookie, new { alias = mesAlias }))
            alias.EnsureSuccessStatusCode();
        for (var i = 0; i < 4; i++)
        {
            using var created = await Send(client, HttpMethod.Post, "/api/logins", mesCookie, new { name = $"alerta{i}" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(createdBody.GetProperty("isOwner").GetBoolean());
            Assert.Equal("Punto de Alerta", createdBody.GetProperty("role").GetString());
            Assert.Equal($"alerta{i}#{mesAlias}", createdBody.GetProperty("displayName").GetString());
        }

        using var fifth = await Send(client, HttpMethod.Post, "/api/logins", mesCookie, new { name = "alertaextra" });
        Assert.Equal(HttpStatusCode.Conflict, fifth.StatusCode);

        var eventoCookie = await SignUp(client, $"evento-{Guid.NewGuid():N}@counter.test");
        await SetPlanCode(factory, eventoCookie, PlanCodes.PackEvento);
        var eventoAlias = $"ev{Guid.NewGuid():N}"[..10];
        using (var alias = await Send(client, HttpMethod.Post, "/api/alias", eventoCookie, new { alias = eventoAlias }))
            alias.EnsureSuccessStatusCode();
        using var second = await Send(client, HttpMethod.Post, "/api/logins", eventoCookie, new { name = "extra" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Sign_out_ends_the_live_session()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var cookie = await SignUp(client, $"out-{Guid.NewGuid():N}@counter.test");

        using var signedOut = await Send(client, HttpMethod.Delete, "/api/sessions", cookie);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        using var after = await Send(client, HttpMethod.Get, "/api/session", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_new_login_replaces_the_previous_session()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();
        var email = $"one-{Guid.NewGuid():N}@counter.test";
        var first = await SignUp(client, email);
        var second = await TestAuth.SignInWithCode(client, factory, email);

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
        await SetPlanCode(factory, ownerCookie, PlanCodes.PackMes);
        var alias = $"rev{Guid.NewGuid():N}"[..10];
        using (var confirmed = await Send(client, HttpMethod.Post, "/api/alias", ownerCookie, new { alias }))
            confirmed.EnsureSuccessStatusCode();
        using var created = await Send(client, HttpMethod.Post, "/api/logins", ownerCookie, new { name = "caja1" });
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var accountId = createdBody.GetProperty("id").GetGuid();
        var token = createdBody.GetProperty("token").GetString()!;
        using var claimed = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "password1" });
        claimed.EnsureSuccessStatusCode();
        var counterCookie = claimed.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
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

    [Fact]
    public async Task Auth0_sign_up_sets_a_cross_site_cookie_and_ignores_a_client_tenant()
    {
        await using var factory = new TransfersApiFactory(sql) { Auth0 = true };
        using var client = factory.CreateClient();
        var email = $"Owner-{Guid.NewGuid():N}@counter.test";

        // Another tenant already exists; the client names it, and the session must still be the new account's own.
        var other = await TestAuth.SignInWithAuth0(factory, $"other-{Guid.NewGuid():N}@counter.test", signUp: true);
        using var otherSession = await Send(client, HttpMethod.Get, "/api/session", other.SessionCookie!);
        var supplied = (await otherSession.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        var run = await TestAuth.SignInWithAuth0(factory, email, signUp: true, suppliedTenant: supplied);

        Assert.Equal(HttpStatusCode.Redirect, run.Callback.StatusCode);
        Assert.Equal("http://localhost:5183/", run.Location);
        var setCookie = run.Callback.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("tc_session=", StringComparison.Ordinal));
        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=None", setCookie, StringComparison.OrdinalIgnoreCase);

        using var session = await Send(client, HttpMethod.Get, "/api/session", run.SessionCookie!);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var body = await session.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email.ToLowerInvariant(), body.GetProperty("email").GetString());
        Assert.True(body.GetProperty("isOwner").GetBoolean());
        Assert.NotEqual(supplied, body.GetProperty("tenantId").GetGuid());
        Assert.NotEqual(Guid.Empty, body.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task Auth0_sign_in_replaces_the_previous_session_and_sign_out_ends_it()
    {
        await using var factory = new TransfersApiFactory(sql) { Auth0 = true };
        using var client = factory.CreateClient();
        var email = $"one-{Guid.NewGuid():N}@counter.test";

        var first = await TestAuth.SignInWithAuth0(factory, email, signUp: true);
        var second = await TestAuth.SignInWithAuth0(factory, email);

        using var oldSession = await Send(client, HttpMethod.Get, "/api/session", first.SessionCookie!);
        using var newSession = await Send(client, HttpMethod.Get, "/api/session", second.SessionCookie!);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newSession.StatusCode);
        var firstTenant = (await (await Send(client, HttpMethod.Get, "/api/session", second.SessionCookie!)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        Assert.NotEqual(Guid.Empty, firstTenant);

        using var signedOut = await Send(client, HttpMethod.Delete, "/api/sessions", second.SessionCookie!);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        using var after = await Send(client, HttpMethod.Get, "/api/session", second.SessionCookie!);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Auth0_refuses_an_unverified_email_a_bad_state_and_a_missing_state_cookie()
    {
        await using var factory = new TransfersApiFactory(sql) { Auth0 = true };

        var unverified = await TestAuth.SignInWithAuth0(factory, $"unverified.{Guid.NewGuid():N}@counter.test", signUp: true);
        Assert.Null(unverified.SessionCookie);
        Assert.EndsWith("/?auth0_error=email_unverified", unverified.Location);

        var badState = await TestAuth.SignInWithAuth0(
            factory, $"state-{Guid.NewGuid():N}@counter.test", signUp: true,
            tamperCallback: path => System.Text.RegularExpressions.Regex.Replace(path, "state=[^&]+", "state=forged"));
        Assert.Null(badState.SessionCookie);
        Assert.EndsWith("/?auth0_error=invalid_state", badState.Location);

        var noCookie = await TestAuth.SignInWithAuth0(factory, $"nocookie-{Guid.NewGuid():N}@counter.test", signUp: true, sendStateCookie: false);
        Assert.Null(noCookie.SessionCookie);
        Assert.EndsWith("/?auth0_error=invalid_state", noCookie.Location);

        var badCode = await TestAuth.SignInWithAuth0(
            factory, $"code-{Guid.NewGuid():N}@counter.test", signUp: true,
            tamperCallback: path => System.Text.RegularExpressions.Regex.Replace(path, "code=[^&]+", "code=forged"));
        Assert.Null(badCode.SessionCookie);
        Assert.EndsWith("/?auth0_error=exchange_failed", badCode.Location);
    }

    [Fact]
    public async Task Auth0_closes_the_local_owner_door_but_not_the_punto_de_alerta_door()
    {
        await using var factory = new TransfersApiFactory(sql) { Auth0 = true };
        using var client = factory.CreateClient();

        using var config = await client.GetAsync("/api/auth0/config");
        Assert.True((await config.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enabled").GetBoolean());
        using var signUp = await client.PostAsJsonAsync("/api/accounts", new { email = $"local-{Guid.NewGuid():N}@counter.test", password = "password1" });
        Assert.Equal(HttpStatusCode.Gone, signUp.StatusCode);
        using var signIn = await client.PostAsJsonAsync("/api/sessions", new { email = "someone@counter.test", password = "password1" });
        Assert.Equal(HttpStatusCode.Gone, signIn.StatusCode);

        var owner = await TestAuth.SignInWithAuth0(factory, $"owner-{Guid.NewGuid():N}@counter.test", signUp: true);
        await SetPlanCode(factory, owner.SessionCookie!, PlanCodes.PackMes);
        var alias = $"a0{Guid.NewGuid():N}"[..10];
        using (var confirmed = await Send(client, HttpMethod.Post, "/api/alias", owner.SessionCookie!, new { alias }))
            confirmed.EnsureSuccessStatusCode();
        using var created = await Send(client, HttpMethod.Post, "/api/logins", owner.SessionCookie!, new { name = "caja1" });
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        using var claimed = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "password1" });
        claimed.EnsureSuccessStatusCode();

        using var alertPoint = await client.PostAsJsonAsync("/api/sessions", new { commerce = alias, user = "caja1", password = "password1" });
        Assert.Equal(HttpStatusCode.OK, alertPoint.StatusCode);

        var asAuth0 = await TestAuth.SignInWithAuth0(factory, $"caja1#{alias}@punto.local");
        Assert.Null(asAuth0.SessionCookie);
        Assert.EndsWith("/?auth0_error=not_allowed", asAuth0.Location);
    }

    [Fact]
    public async Task Auth0_stays_off_without_an_authority_and_the_local_door_keeps_working()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var config = await client.GetAsync("/api/auth0/config");
        Assert.False((await config.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enabled").GetBoolean());
        using var login = await client.GetAsync("/api/auth0/login");
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.NotEmpty(await SignUp(client, $"local-{Guid.NewGuid():N}@counter.test"));
    }

    private static async Task<string> SignUp(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts", new { email, password = "password1" });
        response.EnsureSuccessStatusCode();
        return CookiePair(response.Headers.GetValues("Set-Cookie").Single());
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
        Assert.NotNull(merchant);
        merchant!.PlanCode = planCode;
        await store.SaveChangesAsync(CancellationToken.None);
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
