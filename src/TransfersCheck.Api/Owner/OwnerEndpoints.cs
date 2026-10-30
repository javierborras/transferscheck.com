using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    public sealed record OwnerTicketBody(string? Ticket);
    public static readonly TimeSpan InspectionWindow = TimeSpan.FromHours(6);
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
        group.MapPost("/sessions/ticket", SignInWithTicket);
        group.MapDelete("/sessions", SignOut);
        group.MapGet("/session", Current).AddEndpointFilter(RequireOwner);
        group.MapGet("/services", ListServices).AddEndpointFilter(RequireOwner);
        group.MapPut("/services/{card}", ReplaceSecret).AddEndpointFilter(RequireOwner);
        group.MapGet("/plans", ListPlans).AddEndpointFilter(RequireOwner);
        group.MapPut("/plans/{code}", UpsertPlan).AddEndpointFilter(RequireOwner);
        group.MapPost("/plans", AddPlan).AddEndpointFilter(RequireOwner);
        group.MapPost("/help/refresh", RefreshHelp).AddEndpointFilter(RequireOwner);
        group.MapGet("/home", Home).AddEndpointFilter(RequireOwner);
        group.MapGet("/clients", Clients).AddEndpointFilter(RequireOwner);
        group.MapGet("/clients/{tenantId:guid}", Client).AddEndpointFilter(RequireOwner);
        group.MapPut("/clients/{tenantId:guid}", UpdateClient).AddEndpointFilter(RequireOwner);
        group.MapGet("/devices", Devices).AddEndpointFilter(RequireOwner);
        group.MapGet("/activity", Activity).AddEndpointFilter(RequireOwner);
        group.MapGet("/billing", Billing).AddEndpointFilter(RequireOwner);
        group.MapGet("/movements", Movements).AddEndpointFilter(RequireOwner);
        group.MapGet("/helpdesk/incidents", Incidents).AddEndpointFilter(RequireOwner);
        group.MapGet("/helpdesk/messages", Messages).AddEndpointFilter(RequireOwner);
        group.MapPost("/helpdesk/messages/{id:guid}/close", CloseMessage).AddEndpointFilter(RequireOwner);
        group.MapGet("/logs", ErrorLogs).AddEndpointFilter(RequireOwner);
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

    /// <summary>
    /// Exchanges a short-lived ticket for the owner cookie. The ticket is not the password.
    /// It is rejected when it is expired or when it would last longer than <see cref="InspectionWindow"/>.
    /// </summary>
    public static string CreateInspectionTicket(string email, string signingKey, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > InspectionWindow)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Inspection ticket must expire within 6 hours.");
        return Issue(email.Trim(), DateTimeOffset.UtcNow.Add(lifetime), signingKey);
    }

    private static IResult SignInWithTicket(
        OwnerTicketBody body,
        IOptions<OwnerOptions> owner,
        IOptions<LoginSessionOptions> session,
        HttpResponse response)
    {
        if (!owner.Value.Enabled)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(body.Ticket)
            || !TryValidate(body.Ticket, session.Value.SigningKey, out var email)
            || !TryReadExpiry(body.Ticket, out var expires)
            || expires - DateTimeOffset.UtcNow > InspectionWindow
            || !string.Equals(email, owner.Value.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Results.Unauthorized();

        response.Cookies.Append(CookieName, body.Ticket, new CookieOptions
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

    private static async Task<IResult> Home(
        TransfersCheckDb db,
        IOwnerSecretStore secrets,
        IOptions<MailOptions> mail,
        IOptions<CheckoutOptions> checkout,
        IOptions<SyncOptions> sync,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var counts = await OwnerDashboardReader.CountsAsync(db, now, sync.Value.IdlePollInterval, cancellationToken);
        var cards = await secrets.ListAsync(cancellationToken);
        bool CardSet(string card) => cards.First(item => item.Card == card).IsSet;
        var tasks = new List<object>();
        AddTask(tasks, "trials_ending", "Pruebas por vencer", counts.TrialsEnding);
        AddTask(tasks, "five_day_pending", "Avisos de cinco días sin enviar", counts.FiveDayPending);
        AddTask(tasks, "mail_unset", "Mail sin configurar", CardSet(OwnerServiceCards.Mail) || mail.Value.Enabled ? 0 : 1);
        AddTask(tasks, "webhook_unsigned", "Firma de webhook sin configurar", string.IsNullOrWhiteSpace(checkout.Value.NotificationSigningKey) ? 1 : 0);
        AddTask(tasks, "checkout_token_unset", "Token cobrador sin configurar", CardSet(OwnerServiceCards.MercadoPagoPlatform) ? 0 : 1);
        return Results.Ok(new
        {
            tasks,
            health = new object[]
            {
                Health("commerces", "Comercios", counts.Commerces),
                Health("connected", "Vinculados a Mercado Pago", counts.Connected),
                Health("rate_limited", "En espera del proveedor", counts.RateLimited),
                Health("push_devices", "Dispositivos con aviso", counts.PushDevices),
                Health("checks_stale", "Consultas demoradas", counts.ChecksStale)
            }
        });
    }

    public sealed record OwnerClientEdit(string? Alias, string? PlanCode);

    private static async Task<IResult> Clients(
        string? q,
        string? plan,
        string? mp,
        string? access,
        string? trial,
        TransfersCheckDb db,
        TimeProvider clock,
        IOptions<SyncOptions> sync,
        CancellationToken cancellationToken)
    {
        if (!Known(mp, "vinculado", "sin_vincular", "reconectar", "espera", "demoradas")
            || !Known(access, "vigente", "vence", "vencido", "sin")
            || !Known(trial, "vigente", "no")
            || (q is not null && q.Length > 256)
            || (plan is not null && plan.Length > 32))
            return Results.BadRequest(new { error = "invalid_filter" });

        var rows = await OwnerDashboardReader.ClientsAsync(
            db,
            new OwnerClientFilter(q, plan, mp, access, trial),
            clock.GetUtcNow(),
            sync.Value.IdlePollInterval,
            cancellationToken);
        return Results.Ok(rows.Select(ClientBody));
    }

    private static async Task<IResult> Client(
        Guid tenantId,
        TransfersCheckDb db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var row = await OwnerDashboardReader.ClientAsync(db, tenantId, clock.GetUtcNow(), cancellationToken);
        return row is null ? Results.NotFound() : Results.Ok(ClientBody(row));
    }

    private static async Task<IResult> UpdateClient(
        Guid tenantId,
        OwnerClientEdit body,
        TransfersCheckDb db,
        IPlanCatalog plans,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (body.Alias is null && body.PlanCode is null)
            return Results.BadRequest(new { error = "edit_required" });

        var merchant = await db.Merchants.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == tenantId, cancellationToken);
        if (merchant is null)
            return Results.NotFound();

        if (body.Alias is not null)
        {
            if (!CommerceAlias.TryNormalize(body.Alias, out var alias))
                return Results.BadRequest(new { error = "invalid_alias" });
            var taken = await db.Merchants.IgnoreQueryFilters()
                .AnyAsync(m => m.Alias == alias && m.Id != tenantId, cancellationToken);
            if (taken)
                return Results.Conflict(new { error = "alias_taken" });
            merchant.Alias = alias;
        }

        if (body.PlanCode is not null)
        {
            var code = body.PlanCode.Trim();
            if (await plans.FindByCodeAsync(code, cancellationToken) is null)
                return Results.BadRequest(new { error = "invalid_plan" });
            merchant.PlanCode = code;
        }

        await db.SaveChangesAsync(cancellationToken);
        var row = await OwnerDashboardReader.ClientAsync(db, tenantId, clock.GetUtcNow(), cancellationToken);
        return row is null ? Results.NotFound() : Results.Ok(ClientBody(row));
    }

    private static async Task<IResult> Devices(TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var rows = await OwnerDashboardReader.DevicesAsync(db, cancellationToken);
        return Results.Ok(rows.Select(row => new
        {
            id = row.Id,
            tenantId = row.TenantId,
            alias = row.Alias,
            email = row.Email,
            accountLabel = row.AccountLabel,
            alertPoint = row.AlertPoint
        }));
    }

    private static async Task<IResult> Activity(
        int? days,
        string? tenantId,
        TransfersCheckDb db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var window = days ?? 30;
        if (window is not (7 or 30 or 90))
            return Results.BadRequest(new { error = "invalid_range" });
        Guid? filter = null;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            if (!Guid.TryParse(tenantId, out var id))
                return Results.BadRequest(new { error = "invalid_tenant" });
            filter = id;
        }

        var activity = await OwnerDashboardReader.ActivityAsync(db, window, filter, clock.GetUtcNow(), cancellationToken);
        if (activity is null)
            return Results.NotFound();
        return Results.Ok(new
        {
            days = activity.Days,
            showSignups = activity.ShowSignups,
            callsRecordedFrom = activity.CallsRecordedFrom,
            points = activity.Points.Select(point => new { date = point.Date, signups = point.Signups, queries = point.Queries })
        });
    }

    private static bool Known(string? value, params string[] allowed)
        => string.IsNullOrWhiteSpace(value) || allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static object ClientBody(OwnerClientRow row) => new
    {
        tenantId = row.TenantId,
        alias = row.Alias,
        email = row.Email,
        planCode = row.PlanCode,
        accessUntil = row.AccessUntil,
        trialEndsAt = row.TrialEndsAt,
        connected = row.Connected,
        paidOrdersCount = row.PaidOrdersCount,
        totalPaidAmount = row.TotalPaidAmount,
        lastPaymentAt = row.LastPaymentAt,
        mercadoPago = row.MercadoPago,
        accessState = row.AccessState,
        tone = row.Tone,
        toneLabel = row.ToneLabel,
        pendingPayment = row.PendingPayment,
        loginCount = row.LoginCount,
        pushDeviceCount = row.PushDeviceCount
    };

    private static async Task<IResult> Movements(
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? offset,
        string? status,
        string? operationType,
        ICheckoutAccessToken accessToken,
        ITransferQuerySource source,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var token = await accessToken.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return Results.Conflict(new { error = "token_not_configured" });
        var startOffset = Math.Clamp(offset ?? 0, 0, 100000);
        if (from is not null && to is not null && from > to)
            return Results.BadRequest(new { error = "invalid_range" });

        try
        {
            var page = await source.QueryAsync(new TokenSet(token, "", DateTimeOffset.UtcNow.AddHours(1), ""), from, to, startOffset, 30, cancellationToken);
            var filtered = page.Items.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(status))
                filtered = filtered.Where(item => string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(operationType))
                filtered = filtered.Where(item => string.Equals(item.OperationType, operationType, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.TypeCode, operationType, StringComparison.OrdinalIgnoreCase));

            var items = filtered.Select(row => new
            {
                id = row.ExternalId,
                externalId = row.ExternalId,
                occurredAt = row.OccurredAt,
                approvedAt = row.ApprovedAt,
                payerName = TransferHistory.DisplayPayer(row.PayerName),
                amount = row.Amount,
                currency = row.Currency,
                status = row.Status,
                statusLabel = StatusWords.Label(row.Status),
                statusDetail = row.StatusDetail,
                statusDetailLabel = StatusWords.DetailLabel(row.StatusDetail),
                typeCode = row.TypeCode,
                operationType = row.OperationType,
                paymentMethodId = row.PaymentMethodId,
                operationLabel = StatusWords.OperationLabel(row.OperationType, row.PaymentMethodId, row.TypeCode),
                coelsaId = row.CoelsaId,
                bankTransferId = row.BankTransferId,
                payerDocument = row.PayerDocument,
                payerEmail = row.PayerEmail,
                originBank = row.OriginBank,
                originAccount = row.OriginAccount,
                description = row.Description
            }).ToArray();

            return Results.Ok(new
            {
                items,
                offset = startOffset,
                nextOffset = page.NextOffset,
                totals = new
                {
                    visibleCount = items.Length,
                    approvedAmount = items.Where(i => i.status == "approved").Sum(i => i.amount)
                }
            });
        }
        catch (RateLimitedException)
        {
            return Results.Json(new { error = "rate_limited" }, statusCode: 429);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A thrown exception becomes an empty 500. The browser then hides it (no CORS headers)
            // and the console shows a connection error instead of this response.
            logger.LogError("Owner Mercado Pago movement query failed ({ExceptionType}).", exception.GetType().Name);
            return Results.Json(new { error = "provider_unavailable" }, statusCode: 502);
        }
    }

    private static async Task<IResult> Incidents(TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var rows = await HelpdeskStore.ListIncidentsAsync(db, cancellationToken);
        return Results.Ok(rows.Select(row => new
        {
            fingerprint = row.Fingerprint,
            count = row.Count,
            releaseTag = row.ReleaseTag,
            sentryId = row.SentryUrl is null ? row.SentryId : null,
            sentryUrl = row.SentryUrl,
            githubIssueNumber = row.GitHubIssueNumber,
            issueUrl = row.IssueUrl,
            pullRequestUrl = row.PullRequestUrl
        }));
    }

    private static async Task<IResult> ErrorLogs(int? take, TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(take ?? 50, 1, 100);
        var rows = await db.ErrorLogs.AsNoTracking()
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return Results.Ok(rows.Select(row => new
        {
            occurredAt = row.OccurredAt,
            correlationId = row.CorrelationId,
            message = row.Message,
            path = row.Path,
            level = row.Level
        }));
    }

    private static async Task<IResult> Messages(TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var rows = await HelpdeskStore.ListMessagesAsync(db, cancellationToken);
        return Results.Ok(rows.Select(MessageBody));
    }

    private static async Task<IResult> CloseMessage(
        Guid id,
        TransfersCheckDb db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var row = await HelpdeskStore.CloseMessageAsync(db, id, clock.GetUtcNow(), cancellationToken);
        return row is null ? Results.NotFound() : Results.Ok(MessageBody(row));
    }

    private static object MessageBody(CommerceMessageRow row) => new
    {
        id = row.Id,
        alias = row.Alias,
        email = row.Email,
        text = row.Text,
        createdAt = row.CreatedAt,
        closedAt = row.ClosedAt
    };

    private static async Task<IResult> Billing(string? tenantId, TransfersCheckDb db, CancellationToken cancellationToken)
    {
        Guid? filter = null;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            if (!Guid.TryParse(tenantId, out var id))
                return Results.BadRequest(new { error = "invalid_tenant" });
            filter = id;
        }

        var rows = await OwnerDashboardReader.BillingAsync(db, filter, cancellationToken);
        return Results.Ok(rows.Select(row => new
        {
            id = row.Id,
            tenantId = row.TenantId,
            alias = row.Alias,
            email = row.Email,
            planCode = row.PlanCode,
            amount = row.Amount,
            currency = row.Currency,
            createdAt = row.CreatedAt,
            paidAt = row.PaidAt,
            activatedAt = row.ActivatedAt,
            targetAccessUntil = row.TargetAccessUntil,
            additionalPoint = row.AdditionalPoint
        }));
    }

    private static void AddTask(List<object> tasks, string code, string title, int count)
    {
        if (count > 0)
            tasks.Add(new { code, title, count });
    }

    private static object Health(string code, string title, int value) => new { code, title, value };

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
        return Results.Ok(rows.Where(plan => plan.Code is PlanCodes.PackMes or PlanCodes.PackEvento).Select(PlanDto));
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
        await Task.CompletedTask;
        return Results.Conflict(new { error = "fixed_plans_only" });
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

    private static bool TryReadExpiry(string token, out DateTimeOffset expires)
    {
        expires = default;
        try
        {
            var plain = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = plain.Split('|');
            if (parts.Length != 3 || !long.TryParse(parts[1], out var unix))
                return false;
            expires = DateTimeOffset.FromUnixTimeSeconds(unix);
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
