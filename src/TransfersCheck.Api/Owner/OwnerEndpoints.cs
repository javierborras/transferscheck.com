using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Owner;

/// <summary>Owner microsite door, plan editor, and services panel (stories 12.1 / 12.2 / 12.3). Not Auth0 (ADR-014).</summary>
public static class OwnerEndpoints
{
    public const string CookieName = "tc_owner";
    private const string CookiePath = "/api/owner";

    public sealed record OwnerCredentials(string? Email, string? Password);
    public sealed record OwnerSecretBody(string? Secret, DateTimeOffset? ExpiresAt);
    public sealed record OwnerPlanBody(
        string? Code,
        string? DisplayName,
        int? Amount,
        string? Currency,
        int? DurationDays,
        int? DurationHours,
        string? FaceCopy,
        bool? EnableCopilotHelp,
        bool? Active);

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/owner");
        group.MapPost("/sessions", SignIn);
        group.MapDelete("/sessions", SignOut);
        group.MapGet("/session", Current).AddEndpointFilter(RequireOwner);
        group.MapGet("/services", ListServices).AddEndpointFilter(RequireOwner);
        group.MapPut("/services/{card}", ReplaceSecret).AddEndpointFilter(RequireOwner);
        group.MapGet("/plans", ListPlans).AddEndpointFilter(RequireOwner);
        group.MapPut("/plans/{code}", UpsertPlan).AddEndpointFilter(RequireOwner);
        group.MapPost("/plans", AddPlan).AddEndpointFilter(RequireOwner);
        group.MapPost("/help/refresh", RefreshHelp).AddEndpointFilter(RequireOwner);
    }

    private static async ValueTask<object?> RequireOwner(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetRequiredService<IOptions<LoginSessionOptions>>().Value;
        var owner = http.RequestServices.GetRequiredService<IOptions<OwnerOptions>>().Value;
        if (!owner.Enabled)
            return Results.NotFound();
        if (!http.Request.Cookies.TryGetValue(CookieName, out var token) || !TryValidate(token, options.SigningKey, out var email))
            return Results.Unauthorized();
        if (!string.Equals(email, owner.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Results.Unauthorized();
        http.Items["ownerEmail"] = email;
        return await next(context);
    }

    private static IResult SignIn(
        OwnerCredentials body,
        IOptions<OwnerOptions> owner,
        IOptions<LoginSessionOptions> session,
        HttpResponse response)
    {
        if (!owner.Value.Enabled)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
            return Results.Unauthorized();
        if (!string.Equals(body.Email.Trim(), owner.Value.Email.Trim(), StringComparison.OrdinalIgnoreCase)
            || !FixedEquals(body.Password, owner.Value.Password))
            return Results.Unauthorized();

        var expires = DateTimeOffset.UtcNow.Add(session.Value.Lifetime);
        response.Cookies.Append(CookieName, Issue(owner.Value.Email.Trim(), expires, session.Value.SigningKey), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = CookiePath,
            Expires = expires,
            IsEssential = true
        });
        return Results.Ok(new { email = owner.Value.Email.Trim() });
    }

    private static IResult SignOut(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = CookiePath
        });
        return Results.NoContent();
    }

    private static IResult Current(HttpContext http)
        => Results.Ok(new { email = (string)http.Items["ownerEmail"]! });

    private static async Task<IResult> ListServices(IOwnerSecretStore secrets, CancellationToken cancellationToken)
    {
        var cards = await secrets.ListAsync(cancellationToken);
        return Results.Ok(cards.Select(card => new
        {
            card = card.Card,
            isSet = card.IsSet,
            lastFour = card.LastFour,
            expiresAt = card.ExpiresAt
        }));
    }

    private static async Task<IResult> ReplaceSecret(
        string card,
        OwnerSecretBody body,
        IOwnerSecretStore secrets,
        CancellationToken cancellationToken)
    {
        if (card == OwnerServiceCards.WhatsApp)
            return Results.Conflict(new { error = "whatsapp_has_no_key" });
        if (!OwnerServiceCards.All.Contains(card))
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(body.Secret))
            return Results.BadRequest(new { error = "secret_required" });

        await secrets.SetAsync(card, body.Secret, body.ExpiresAt, cancellationToken);
        var status = (await secrets.ListAsync(cancellationToken)).First(item => item.Card == card);
        return Results.Ok(new
        {
            card = status.Card,
            isSet = status.IsSet,
            lastFour = status.LastFour,
            expiresAt = status.ExpiresAt
        });
    }

    private static async Task<IResult> ListPlans(IPlanCatalog plans, CancellationToken cancellationToken)
    {
        var rows = await plans.ListAllAsync(cancellationToken);
        return Results.Ok(rows.Select(PlanDto));
    }

    private static async Task<IResult> UpsertPlan(
        string code,
        OwnerPlanBody body,
        IPlanCatalog plans,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 32)
            return Results.BadRequest(new { error = "code_required" });
        var existing = await plans.FindByCodeAsync(code, cancellationToken);
        if (existing is null)
            return Results.NotFound();
        var plan = Apply(existing, body);
        plan.Code = existing.Code;
        await plans.UpsertAsync(plan, cancellationToken);
        return Results.Ok(PlanDto(await plans.FindByCodeAsync(plan.Code, cancellationToken) ?? plan));
    }

    private static async Task<IResult> AddPlan(
        OwnerPlanBody body,
        IPlanCatalog plans,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Code))
            return Results.BadRequest(new { error = "code_required" });
        var code = body.Code.Trim();
        if (code.Length > 32)
            return Results.BadRequest(new { error = "code_too_long" });
        if (await plans.FindByCodeAsync(code, cancellationToken) is not null)
            return Results.Conflict(new { error = "code_exists" });
        if (string.IsNullOrWhiteSpace(body.DisplayName) || body.Amount is null)
            return Results.BadRequest(new { error = "display_and_amount_required" });

        var plan = Apply(new Plan { Code = code, Currency = "ARS", Active = true }, body);
        await plans.UpsertAsync(plan, cancellationToken);
        return Results.Ok(PlanDto(plan));
    }

    private static async Task<IResult> RefreshHelp(
        IHelpCorpus corpus,
        Microsoft.Extensions.Options.IOptions<HelpCopilotOptions> options,
        IWebHostEnvironment env,
        CancellationToken cancellationToken)
    {
        var configured = options.Value.CorpusPath;
        var folder = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "web", "help"))
            : Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

        var previous = await corpus.GetPublishedVersionAsync(cancellationToken);
        var drafts = HelpCorpusFiles.ReadFolder(folder);
        if (drafts.Count == 0)
            return Results.Ok(new { published = false, version = previous, error = "empty_or_missing_folder", folder });

        var result = await corpus.PublishAsync(drafts, cancellationToken);
        // ADR-016: a failed refresh leaves production up and keeps the previous index.
        return Results.Ok(new
        {
            published = result.Published,
            version = result.Published ? result.Version : previous,
            previousVersion = previous,
            error = result.Error,
            folder
        });
    }

    private static Plan Apply(Plan baseline, OwnerPlanBody body)
        => new()
        {
            Code = baseline.Code,
            DisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? baseline.DisplayName : body.DisplayName.Trim(),
            Amount = body.Amount ?? baseline.Amount,
            Currency = string.IsNullOrWhiteSpace(body.Currency) ? baseline.Currency : body.Currency.Trim().ToUpperInvariant(),
            DurationDays = body.DurationDays ?? baseline.DurationDays,
            DurationHours = body.DurationHours ?? baseline.DurationHours,
            FaceCopy = body.FaceCopy ?? baseline.FaceCopy,
            EnableCopilotHelp = body.EnableCopilotHelp ?? baseline.EnableCopilotHelp,
            Active = body.Active ?? baseline.Active
        };

    private static object PlanDto(Plan plan)
        => new
        {
            code = plan.Code,
            displayName = plan.DisplayName,
            amount = plan.Amount,
            currency = plan.Currency,
            durationDays = plan.DurationDays,
            durationHours = plan.DurationHours,
            faceCopy = plan.FaceCopy,
            enableCopilotHelp = plan.EnableCopilotHelp,
            active = plan.Active
        };

    private static string Issue(string email, DateTimeOffset expires, string signingKey)
    {
        var payload = $"{email}|{expires.ToUnixTimeSeconds()}";
        var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(payload)));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));
    }

    private static bool TryValidate(string token, string signingKey, out string email)
    {
        email = "";
        try
        {
            var plain = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = plain.Split('|');
            if (parts.Length != 3 || !long.TryParse(parts[1], out var unix) || DateTimeOffset.FromUnixTimeSeconds(unix) <= DateTimeOffset.UtcNow)
                return false;
            var payload = $"{parts[0]}|{parts[1]}";
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(payload)));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2])))
                return false;
            email = parts[0];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
