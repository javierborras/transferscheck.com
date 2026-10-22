using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Accounts;

public static class AccountEndpoints
{
    public sealed record Credentials(string? Email, string? Password, Guid? TenantId);
    public sealed record PushApproval(bool Approved);

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api");
        group.MapPost("/accounts", SignUp);
        group.MapPost("/sessions", SignIn);
        group.MapDelete("/sessions", SignOut).AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/session", Current).AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/logins", List).AddEndpointFilter<LiveSessionFilter>();
        group.MapPost("/logins", Create).AddEndpointFilter<LiveSessionFilter>();
        group.MapDelete("/logins/{id:guid}", Revoke).AddEndpointFilter<LiveSessionFilter>();
        group.MapPut("/logins/{id:guid}/push", SetPush).AddEndpointFilter<LiveSessionFilter>();
    }

    private static async Task<IResult> SignUp(
        Credentials body,
        IMerchantStore store,
        IOptions<LoginSessionOptions> sessionOptions,
        ITenantContext tenantContext,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!Valid(body))
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
            IsOwner = true
        };
        await store.AddAccountAsync(account, cancellationToken);
        await Issue(store, tenantContext, sessionOptions.Value, clock, response, account, cancellationToken);
        return Results.Created("/api/session", new { email, isOwner = true });
    }

    private static async Task<IResult> SignIn(
        Credentials body,
        IMerchantStore store,
        IOptions<LoginSessionOptions> sessionOptions,
        ITenantContext tenantContext,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
            return Results.Unauthorized();

        var account = await store.FindAccountByEmailAsync(body.Email, cancellationToken);
        if (account is null || account.Revoked || !PasswordHasher.Verify(body.Password, account.PasswordHash))
            return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

        await Issue(store, tenantContext, sessionOptions.Value, clock, response, account, cancellationToken);
        return Results.Ok(new { email = account.Email, isOwner = account.IsOwner });
    }

    private static IResult SignOut(IOptions<LoginSessionOptions> sessionOptions, HttpResponse response)
    {
        response.Cookies.Delete(sessionOptions.Value.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/"
        });
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
            isOwner = account.IsOwner
        });
    }

    private static async Task<IResult> List(HttpContext http, IMerchantStore store, CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var accounts = await store.ListAccountsAsync(cancellationToken);
        return Results.Ok(accounts.Select(a => new
        {
            id = a.Id,
            email = a.Email,
            isOwner = a.IsOwner,
            pushApproved = a.PushApproved,
            revoked = a.Revoked
        }));
    }

    private static async Task<IResult> Create(
        Credentials body,
        HttpContext http,
        IMerchantStore store,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!Valid(body))
            return Results.BadRequest(new { error = "invalid_account" });
        if (await store.CountAccountsAsync(cancellationToken) >= 5)
            return Results.Conflict(new { error = "account_cap" });

        var email = body.Email!.Trim().ToLowerInvariant();
        if (await store.FindAccountByEmailAsync(email, cancellationToken) is not null)
            return Results.Conflict(new { error = "email_taken" });

        var account = new LoginAccount
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant(http),
            Email = email,
            PasswordHash = PasswordHasher.Hash(body.Password!),
            IsOwner = false
        };
        await store.AddAccountAsync(account, cancellationToken);
        return Results.Created($"/api/logins/{account.Id}", new { id = account.Id, email });
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

    private static async Task Issue(
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

    private static bool Valid(Credentials body)
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
