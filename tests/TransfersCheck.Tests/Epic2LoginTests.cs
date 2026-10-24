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

    [Fact]
    public async Task Local_door_validates_sign_up_refuses_duplicates_and_bad_credentials()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var client = factory.CreateClient();

        foreach (var (email, password) in new[]
                 {
                     ("", "password1"), ("no-at-sign", "password1"), ("has space@counter.test", "password1"),
                     ("short@counter.test", "short"), ("empty@counter.test", ""), (new string('a', 250) + "@counter.test", "password1")
                 })
        {
            using var invalid = await client.PostAsJsonAsync("/api/accounts", new { email, password });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_account", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }

        var address = $"dup-{Guid.NewGuid():N}@counter.test";
        await SignUp(client, address);
        using var duplicate = await client.PostAsJsonAsync("/api/accounts", new { email = address.ToUpperInvariant(), password = "password1" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("email_taken", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        using var wrongPassword = await client.PostAsJsonAsync("/api/sessions", new { email = address, password = "not-the-password" });
        using var unknownEmail = await client.PostAsJsonAsync("/api/sessions", new { email = $"nobody-{Guid.NewGuid():N}@counter.test", password = "password1" });
        using var noPassword = await client.PostAsJsonAsync("/api/sessions", new { email = address });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("invalid_credentials", (await wrongPassword.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal("invalid_credentials", (await unknownEmail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, noPassword.StatusCode);

        // The right password still needs the mailed code; a wrong code is refused.
        using var wrongCode = await client.PostAsJsonAsync("/api/sessions", new { email = address, password = "password1", code = "000000" });
        Assert.Equal("invalid_code", (await wrongCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Session_endpoint_refuses_a_missing_or_forged_cookie_and_survives_a_reload_with_the_real_one()
    {
        await using var factory = new TransfersApiFactory(sql) { Auth0 = true };
        using var client = factory.CreateClient();

        using var none = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        using var forged = await Send(client, HttpMethod.Get, "/api/session", "tc_session=not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);

        var run = await TestAuth.SignInWithAuth0(factory, $"reload-{Guid.NewGuid():N}@counter.test", signUp: true);
        for (var reload = 0; reload < 3; reload++)
        {
            using var again = await Send(client, HttpMethod.Get, "/api/session", run.SessionCookie!);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        // Signing out twice with the same cookie is refused the second time, never a 500.
        using var out1 = await Send(client, HttpMethod.Delete, "/api/sessions", run.SessionCookie!);
        using var out2 = await Send(client, HttpMethod.Delete, "/api/sessions", run.SessionCookie!);
        Assert.Equal(HttpStatusCode.NoContent, out1.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, out2.StatusCode);
    }

    [Fact]
    public async Task Auth0_sign_up_for_an_existing_email_reuses_the_account_and_a_revoked_owner_is_refused()
    {
        await using var factory = new TransfersApiFactory(sql) { Auth0 = true };
        using var client = factory.CreateClient();
        var email = $"again-{Guid.NewGuid():N}@counter.test";

        var first = await TestAuth.SignInWithAuth0(factory, email, signUp: true);
        var again = await TestAuth.SignInWithAuth0(factory, email, signUp: true);
        Assert.NotNull(again.SessionCookie);
        var tenantA = (await (await Send(client, HttpMethod.Get, "/api/session", again.SessionCookie!)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.TenantId = tenantA;
        var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
        var account = await store.FindAccountByEmailAsync(email, CancellationToken.None);
        Assert.NotNull(account);
        Assert.False(account!.PasswordSet);
        Assert.True(account.IsOwner);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, HttpMethod.Get, "/api/session", first.SessionCookie!)).StatusCode);

        account.Revoked = true;
        await store.SaveChangesAsync(CancellationToken.None);
        var refused = await TestAuth.SignInWithAuth0(factory, email);
        Assert.Null(refused.SessionCookie);
        Assert.EndsWith("/?auth0_error=not_allowed", refused.Location);
    }

    [Fact]
    public async Task Punto_de_alerta_door_checks_the_invite_the_credentials_and_revocation()
    {
        await using var factory = new TransfersApiFactory(sql) { Auth0 = true };
        using var client = factory.CreateClient();
        var owner = await TestAuth.SignInWithAuth0(factory, $"owner-{Guid.NewGuid():N}@counter.test", signUp: true);
        await SetPlanCode(factory, owner.SessionCookie!, PlanCodes.PackMes);
        var alias = $"pa{Guid.NewGuid():N}"[..10];
        using (var confirmed = await Send(client, HttpMethod.Post, "/api/alias", owner.SessionCookie!, new { alias }))
            confirmed.EnsureSuccessStatusCode();

        using var badName = await Send(client, HttpMethod.Post, "/api/logins", owner.SessionCookie!, new { name = "no good!" });
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
        using var created = await Send(client, HttpMethod.Post, "/api/logins", owner.SessionCookie!, new { name = "Caja1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var accountId = createdBody.GetProperty("id").GetGuid();
        var token = createdBody.GetProperty("token").GetString()!;
        using var duplicate = await Send(client, HttpMethod.Post, "/api/logins", owner.SessionCookie!, new { name = "caja1" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        async Task<string?> Error(HttpResponseMessage response)
            => (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("error", out var error) ? error.GetString() : null;
        Task<HttpResponseMessage> LogIn(string commerce, string user, string password)
            => client.PostAsJsonAsync("/api/sessions", new { commerce, user, password });

        // Invite not claimed yet: no password exists, so the door stays shut.
        using (var early = await LogIn(alias, "caja1", "password1"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);
            Assert.Equal("invalid_credentials", await Error(early));
        }

        using (var shortPassword = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "short" }))
            Assert.Equal("invalid_password", await Error(shortPassword));
        using (var unknown = await client.PostAsJsonAsync($"/api/invites/{new string('0', 48)}", new { password = "password1" }))
            Assert.Equal("invalid_invite", await Error(unknown));
        using var claimed = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "password1" });
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
        var claimedCookie = claimed.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using (var replay = await client.PostAsJsonAsync($"/api/invites/{token}", new { password = "otherpass1" }))
            Assert.Equal("invalid_invite", await Error(replay));

        foreach (var (commerce, user, password) in new[]
                 {
                     (alias, "caja1", "wrong-password"), (alias, "nadie", "password1"), ("otrocomercio", "caja1", "password1"),
                     (alias, "bad user!", "password1"), ("!!", "caja1", "password1")
                 })
        {
            using var denied = await LogIn(commerce, user, password);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal("invalid_credentials", await Error(denied));
        }

        using var signedIn = await LogIn(alias.ToUpperInvariant(), "CAJA1", "password1");
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        var body = await signedIn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"caja1#{alias}", body.GetProperty("displayName").GetString());
        Assert.False(body.GetProperty("isOwner").GetBoolean());
        var cookie = signedIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        // The invite-claim session was replaced by the new login.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, HttpMethod.Get, "/api/session", claimedCookie)).StatusCode);
        using var session = await Send(client, HttpMethod.Get, "/api/session", cookie);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal("caja1", (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userName").GetString());
        // A Punto de Alerta never reaches the admin screens.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Get, "/api/logins", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Post, "/api/alias", cookie, new { alias = "hackhack" })).StatusCode);

        using var signedOut = await Send(client, HttpMethod.Delete, "/api/sessions", cookie);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        using var again = await LogIn(alias, "caja1", "password1");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        using var revoked = await Send(client, HttpMethod.Delete, $"/api/logins/{accountId}", owner.SessionCookie!);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var afterRevoke = await LogIn(alias, "caja1", "password1");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
        Assert.Equal("invalid_credentials", await Error(afterRevoke));
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
