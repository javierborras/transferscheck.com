using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Accounts;

public static class AccountEndpoints
{
    public sealed record Credentials(string? Email, string? Password, string? Code, string? Commerce, string? User, Guid? TenantId);
    public sealed record AliasBody(string? Alias);
    public sealed record InviteName(string? Name);
    public sealed record InvitePassword(string? Password);
    public sealed record PushApproval(bool Approved);

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api");
        group.MapPost("/accounts", SignUp);
        group.MapPost("/sessions", SignIn);
        group.MapDelete("/sessions", SignOut).AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/session", Current).AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/alias", GetAlias).AddEndpointFilter<LiveSessionFilter>();
        group.MapPost("/alias", ConfirmAlias).AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/logins", List).AddEndpointFilter<LiveSessionFilter>();
        group.MapPost("/logins", Create).AddEndpointFilter<LiveSessionFilter>();
        group.MapDelete("/logins/{id:guid}", Revoke).AddEndpointFilter<LiveSessionFilter>();
        group.MapPut("/logins/{id:guid}/push", SetPush).AddEndpointFilter<LiveSessionFilter>();
        group.MapPost("/invites/{token}", ClaimInvite);
    }

    private static async Task<IResult> SignUp(
        Credentials body,
        IMerchantStore store,
        IOptions<LoginSessionOptions> sessionOptions,
        IOptions<Auth0Options> auth0,
        ITenantContext tenantContext,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        // ADR-018: commerce identity is Auth0 whenever a tenant is configured.
        if (auth0.Value.Enabled)
            return Results.Json(new { error = "use_auth0" }, statusCode: StatusCodes.Status410Gone);

        if (!ValidSignUp(body))
            return Results.BadRequest(new { error = "invalid_account" });

        var email = body.Email!.Trim().ToLowerInvariant();
        if (await store.FindAccountByEmailAsync(email, cancellationToken) is not null)
            return Results.Conflict(new { error = "email_taken" });

        var now = clock.GetUtcNow();
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            ChecklistStep = ChecklistRules.Step(true, false, false)
        };
        await store.AddMerchantAsync(merchant, cancellationToken);

        var account = new LoginAccount
        {
            Id = Guid.NewGuid(),
            TenantId = merchant.Id,
            Email = email,
            PasswordHash = PasswordHasher.Hash(body.Password!),
            IsOwner = true,
            PasswordSet = true
        };
        await store.AddAccountAsync(account, cancellationToken);
        await Issue(store, tenantContext, sessionOptions.Value, clock, response, account, cancellationToken);
        return Results.Created("/api/session", new { email, isOwner = true });
    }

    private static async Task<IResult> SignIn(
        Credentials body,
        IMerchantStore store,
        IEmailCodeStore codes,
        ICommerceAliasStore aliases,
        IMailSender mail,
        IOptions<LoginSessionOptions> sessionOptions,
        IOptions<Auth0Options> auth0,
        ITenantContext tenantContext,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Password))
            return Results.Unauthorized();

        LoginAccount? account;
        var alertPointDoor = !string.IsNullOrWhiteSpace(body.Commerce) && !string.IsNullOrWhiteSpace(body.User);
        if (alertPointDoor)
        {
            if (!CommerceAlias.TryNormalize(body.Commerce, out var alias)
                || !AlertPointIdentity.TryNormalizeUser(body.User, out var userName))
                return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

            account = await store.FindAccountByEmailAsync(AlertPointIdentity.EmailKey(userName, alias), cancellationToken);
            if (account is null || account.Revoked || account.IsOwner || !account.PasswordSet
                || !PasswordHasher.Verify(body.Password, account.PasswordHash))
                return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

            await Issue(store, tenantContext, sessionOptions.Value, clock, response, account, cancellationToken);
            return Results.Ok(new { email = account.Email, isOwner = false, displayName = AlertPointIdentity.Display(userName, alias) });
        }

        // Punto de Alerta (above) stays usuario/clave. The commerce admin door is Auth0 (ADR-018).
        if (auth0.Value.Enabled)
            return Results.Json(new { error = "use_auth0" }, statusCode: StatusCodes.Status410Gone);

        if (string.IsNullOrWhiteSpace(body.Email))
            return Results.Unauthorized();

        account = await store.FindAccountByEmailAsync(body.Email, cancellationToken);
        if (account is null || account.Revoked || !account.PasswordSet
            || !PasswordHasher.Verify(body.Password, account.PasswordHash))
            return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

        if (!account.IsOwner)
            return Results.Json(new { error = "use_commerce_user" }, statusCode: StatusCodes.Status401Unauthorized);

        var now = clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(body.Code))
        {
            var issued = EmailSignInCode.Generate();
            await codes.IssueAsync(account.Id, account.Email, issued, now.Add(sessionOptions.Value.EmailCodeLifetime), cancellationToken);
            await mail.SendAsync(
                account.Email,
                "TransfersCheck — código de acceso",
                issued,
                cancellationToken);
            return Results.Json(new { error = "code_required" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!await codes.TryConsumeAsync(account.Id, body.Code.Trim(), now, cancellationToken))
            return Results.Json(new { error = "invalid_code" }, statusCode: StatusCodes.Status401Unauthorized);

        await Issue(store, tenantContext, sessionOptions.Value, clock, response, account, cancellationToken);
        var confirmedAlias = await aliases.GetAsync(account.TenantId, cancellationToken);
        return Results.Ok(new
        {
            email = account.Email,
            isOwner = true,
            alias = confirmedAlias is null ? null : CommerceAlias.Display(confirmedAlias)
        });
    }

    private static async Task<IResult> SignOut(
        HttpContext http,
        IMerchantStore store,
        IOptions<LoginSessionOptions> sessionOptions,
        IOptions<Auth0Options> auth0,
        IOptions<UiOptions> ui,
        IIdentityProvider identity,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var accountId = Guid.Parse(http.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var account = await store.FindAccountAsync(accountId, cancellationToken);
        if (account is null)
            return Results.Unauthorized();

        if (account.LiveSessionId is { } sessionId)
        {
            var session = await store.FindSessionAsync(sessionId, cancellationToken);
            if (session is not null)
                session.ReplacedAt = clock.GetUtcNow();
        }

        account.LiveSessionId = null;
        await store.SaveChangesAsync(cancellationToken);
        response.Cookies.Delete(sessionOptions.Value.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/"
        });

        // Commerce admins who arrived through Auth0 must also end the Universal Login SSO session (shared counters).
        if (auth0.Value.Enabled && account.IsOwner && !string.IsNullOrEmpty(account.Auth0Subject))
        {
            var returnTo = !string.IsNullOrWhiteSpace(auth0.Value.LogoutReturnUri)
                ? auth0.Value.LogoutReturnUri.TrimEnd('/')
                : (ui.Value.Origin ?? "").TrimEnd('/');
            if (string.IsNullOrEmpty(returnTo))
                returnTo = "/";
            return Results.Ok(new { federatedLogoutUrl = identity.BuildLogoutUrl(returnTo) });
        }

        return Results.NoContent();
    }

    private static IResult Current(HttpContext http, ITenantContext tenant)
    {
        var account = (LoginAccount)http.Items["account"]!;
        return Results.Ok(new
        {
            accountId = account.Id,
            tenantId = tenant.TenantId,
            email = account.Email,
            isOwner = account.IsOwner,
            userName = account.UserName
        });
    }

    private static async Task<IResult> GetAlias(
        HttpContext http,
        ICommerceAliasStore aliases,
        CancellationToken cancellationToken)
    {
        var alias = await aliases.GetAsync(Tenant(http), cancellationToken);
        return Results.Ok(new { alias = alias is null ? null : CommerceAlias.Display(alias) });
    }

    private static async Task<IResult> ConfirmAlias(
        AliasBody body,
        HttpContext http,
        ICommerceAliasStore aliases,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (!CommerceAlias.TryNormalize(body.Alias, out var normalized))
            return Results.BadRequest(new { error = "invalid_alias" });
        var result = await aliases.ConfirmAsync(Tenant(http), normalized, cancellationToken);
        return result switch
        {
            ConfirmAliasResult.Confirmed => Results.Ok(new { alias = CommerceAlias.Display(normalized) }),
            ConfirmAliasResult.AlreadySet => Results.Conflict(new { error = "alias_locked" }),
            ConfirmAliasResult.Taken => Results.Conflict(new { error = "alias_taken" }),
            _ => Results.BadRequest(new { error = "invalid_alias" })
        };
    }

    private static async Task<IResult> List(
        HttpContext http,
        IMerchantStore store,
        ICommerceAliasStore aliases,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var alias = await aliases.GetAsync(Tenant(http), cancellationToken);
        var accounts = await store.ListAccountsAsync(cancellationToken);
        return Results.Ok(accounts.Select(a => new
        {
            id = a.Id,
            email = a.Email,
            displayName = a.IsOwner || string.IsNullOrEmpty(a.UserName) || alias is null
                ? a.Email
                : AlertPointIdentity.Display(a.UserName, alias),
            isOwner = a.IsOwner,
            role = a.IsOwner ? "Admin" : "Punto de Alerta",
            pushApproved = a.PushApproved,
            revoked = a.Revoked,
            passwordSet = a.PasswordSet
        }));
    }

    private static async Task<IResult> Create(
        InviteName body,
        HttpContext http,
        IMerchantStore store,
        ICommerceAliasStore aliases,
        IAlertInviteStore invites,
        ITenantPlanCode planCode,
        LoginAccountCap accountCap,
        IOptions<LoginSessionOptions> sessionOptions,
        IOptions<UiOptions> ui,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var alias = await aliases.GetAsync(Tenant(http), cancellationToken);
        if (alias is null)
            return Results.Conflict(new { error = "alias_required" });
        if (!AlertPointIdentity.TryNormalizeUser(body.Name, out var userName))
            return Results.BadRequest(new { error = "invalid_name" });

        var accounts = await store.ListAccountsAsync(cancellationToken);
        var alertPoints = accounts.Count(a => !a.IsOwner && !a.Revoked);
        if (!accountCap.AllowsAnotherAlertPoint(await planCode.GetAsync(cancellationToken), alertPoints))
            return Results.Conflict(new { error = "account_cap" });

        var email = AlertPointIdentity.EmailKey(userName, alias);
        if (await store.FindAccountByEmailAsync(email, cancellationToken) is not null)
            return Results.Conflict(new { error = "name_taken" });

        var account = new LoginAccount
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant(http),
            Email = email,
            UserName = userName,
            PasswordHash = PasswordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
            IsOwner = false,
            PasswordSet = false
        };
        await store.AddAccountAsync(account, cancellationToken);

        var token = AlertPointIdentity.NewInviteToken();
        var now = clock.GetUtcNow();
        await invites.AddAsync(new AlertInvite
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            AccountId = account.Id,
            Token = token,
            ExpiresAt = now.Add(sessionOptions.Value.InviteLinkLifetime)
        }, cancellationToken);

        var origin = (ui.Value.Origin ?? "").TrimEnd('/');
        var inviteUrl = $"{origin}{AlertPointIdentity.InvitePath(token)}";
        var displayName = AlertPointIdentity.Display(userName, alias);
        return Results.Created($"/api/logins/{account.Id}", new
        {
            id = account.Id,
            displayName,
            isOwner = false,
            role = "Punto de Alerta",
            inviteUrl,
            waMeUrl = AlertPointIdentity.WaMeUrl(inviteUrl),
            token
        });
    }

    private static async Task<IResult> ClaimInvite(
        string token,
        InvitePassword body,
        IMerchantStore store,
        IAlertInviteStore invites,
        IOptions<LoginSessionOptions> sessionOptions,
        ITenantContext tenantContext,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 8)
            return Results.BadRequest(new { error = "invalid_password" });

        var invite = await invites.FindByTokenAsync(token, cancellationToken);
        var now = clock.GetUtcNow();
        if (invite is null || invite.ConsumedAt is not null || invite.ExpiresAt <= now)
            return Results.Json(new { error = "invalid_invite" }, statusCode: StatusCodes.Status401Unauthorized);

        tenantContext.TenantId = invite.TenantId;
        var account = await store.FindAccountAsync(invite.AccountId, cancellationToken);
        if (account is null || account.Revoked || account.PasswordSet)
            return Results.Json(new { error = "invalid_invite" }, statusCode: StatusCodes.Status401Unauthorized);

        account.PasswordHash = PasswordHasher.Hash(body.Password);
        account.PasswordSet = true;
        invite.ConsumedAt = now;
        await invites.SaveChangesAsync(cancellationToken);
        await Issue(store, tenantContext, sessionOptions.Value, clock, response, account, cancellationToken);
        return Results.Ok(new { email = account.Email, isOwner = false });
    }

    private static async Task<IResult> Revoke(
        Guid id,
        HttpContext http,
        IMerchantStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var account = await store.FindAccountAsync(id, cancellationToken);
        if (account is null)
            return Results.NotFound();
        if (account.IsOwner)
            return Results.Conflict(new { error = "owner_stays" });

        account.Revoked = true;
        if (account.LiveSessionId is { } sessionId)
        {
            var session = await store.FindSessionAsync(sessionId, cancellationToken);
            if (session is not null)
                session.ReplacedAt = clock.GetUtcNow();
        }
        account.LiveSessionId = null;
        await store.DeletePushDevicesForAccountAsync(id, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SetPush(
        Guid id,
        PushApproval body,
        HttpContext http,
        IMerchantStore store,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var account = await store.FindAccountAsync(id, cancellationToken);
        if (account is null || account.Revoked)
            return Results.NotFound();
        account.PushApproved = body.Approved;
        await store.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    internal static async Task Issue(
        IMerchantStore store,
        ITenantContext tenantContext,
        LoginSessionOptions options,
        TimeProvider clock,
        HttpResponse response,
        LoginAccount account,
        CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
            throw new InvalidOperationException("Session:SigningKey must be at least 32 bytes.");

        tenantContext.TenantId = account.TenantId;
        var now = clock.GetUtcNow();
        if (account.LiveSessionId is { } previousId)
        {
            var previous = await store.FindSessionAsync(previousId, cancellationToken);
            if (previous is not null)
                previous.ReplacedAt = now;
        }

        var session = new LoginSession
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            AccountId = account.Id,
            ExpiresAt = now.Add(options.Lifetime)
        };
        account.LiveSessionId = session.Id;
        await store.AddSessionAsync(session, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        var token = SessionIssuer.Issue(account, session.Id, session.ExpiresAt, options.SigningKey);
        SessionIssuer.AppendCookie(response, options, token);
    }

    private static bool ValidSignUp(Credentials body)
        => !string.IsNullOrWhiteSpace(body.Email)
           && body.Email.Contains('@')
           && !body.Email.Contains(' ')
           && body.Email.Trim().Length <= 256
           && !string.IsNullOrWhiteSpace(body.Password)
           && body.Password.Length >= 8;

    private static bool Owner(HttpContext http)
        => http.User.FindFirst("owner")?.Value == "1";

    private static Guid Tenant(HttpContext http)
        => Guid.Parse(http.User.FindFirst("tid")!.Value);
}
