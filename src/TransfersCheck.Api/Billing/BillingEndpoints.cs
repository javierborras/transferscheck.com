using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using TransfersCheck.Infrastructure;
using TransfersCheck.Api.Auth;
using TransfersCheck.Api.Jobs;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Billing;

public static class BillingEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        app.MapGet("/api/plans", async (IPlanCatalog plans, CancellationToken ct) => Results.Ok(new {
            plans = (await plans.ListActiveAsync(ct)).Where(IsFixed).Select(plan => PublicPlan(plan)),
            additionalPointPrice = AlertPointAccess.AdditionalPrice
        }));
        group.MapPost("/billing/initial-plan", InitialPlan);
        group.MapGet("/billing", Price);
        group.MapGet("/referral", Referral);
        group.MapPost("/billing/checkout", Checkout);
        group.MapPost("/billing/start-event", StartEvent);
        app.MapPost("/api/billing/webhook", Webhook);
        app.MapGet("/api/billing/return", (IOptions<UiOptions> ui) => Results.Redirect(ui.Value.Origin.TrimEnd('/') + "/pagar?returned=true"));
    }

    private static bool IsFixed(Plan plan) => plan.Code is PlanCodes.PackMes or PlanCodes.PackEvento;
    private static object PublicPlan(Plan plan, int renewing = 0) => new {
        code = plan.Code, displayName = plan.Code == PlanCodes.PackEvento ? "Individual / Evento" : plan.DisplayName,
        amount = plan.Amount, currency = plan.Currency, durationDays = plan.DurationDays, durationHours = plan.DurationHours,
        faceCopy = plan.Code == PlanCodes.PackEvento ? "24 horas desde que iniciás el evento. Admin + 1 Punto de Alerta." : "Para el comercio que cobra todos los días.",
        enableCopilotHelp = plan.EnableCopilotHelp, includedAlertPoints = AlertPointAccess.Included(plan.Code),
        additionalPointPrice = AlertPointAccess.AdditionalPrice, renewingPoints = renewing,
        renewalAmount = checked(plan.Amount + renewing * AlertPointAccess.AdditionalPrice)
    };

    private sealed record InitialPlanBody(string? PlanCode);
    private static async Task<IResult> InitialPlan(InitialPlanBody body, HttpContext http, IMerchantStore store, IPlanCatalog plans, CancellationToken ct)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner) return Results.StatusCode(403);
        var plan = await plans.FindByCodeAsync(body.PlanCode ?? "", ct);
        if (plan is null || !plan.Active || !IsFixed(plan)) return Results.BadRequest();
        var merchant = await store.FindMerchantAsync(account.TenantId, ct);
        if (merchant is null || merchant.ConnectedAt is not null || merchant.AccessUntil is not null) return Results.Conflict();
        merchant.PlanCode = plan.Code; await store.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static async Task<IResult> Referral(HttpContext http, IReferralStore referrals, CancellationToken ct)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner) return Results.StatusCode(403);
        return Results.Ok(new { label = "Mi código", code = await referrals.GetOrCreateCodeAsync(account.TenantId, ct), cashbackBalance = (decimal?)null });
    }

    private static async Task<IResult> Price(HttpContext http, IMerchantStore store, IPlanCatalog plans, TransfersCheckDb db, TimeProvider clock, CancellationToken ct)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var merchant = await store.FindMerchantAsync(account.TenantId, ct);
        var active = (await plans.ListActiveAsync(ct)).Where(IsFixed).ToArray();
        var accounts = account.IsOwner ? await store.ListAccountsAsync(ct) : [];
        var ready = await db.BillingOrders.AsNoTracking().Where(order => order.PlanCode == PlanCodes.PackEvento && order.AdditionalPointId == null && order.PaidAt != null && order.ActivatedAt == null).OrderBy(order => order.CreatedAt).ToListAsync(ct);
        return Results.Ok(new {
            accountLabel = "Email", email = account.Email, planLabel = "Plan", planCode = merchant?.PlanCode,
            planName = active.FirstOrDefault(plan => plan.Code == merchant?.PlanCode)?.DisplayName,
            connected = merchant?.ConnectedAt != null,
            accessUntil = merchant?.AccessUntil, serverNow = clock.GetUtcNow(),
            trialEligible = PackMesTrial.Applies(merchant?.PlanCode) && merchant?.TrialEndsAt is null && merchant?.AccessUntil is null,
            alertPointCap = AlertPointAccess.Included(merchant?.PlanCode), additionalPointPrice = AlertPointAccess.AdditionalPrice,
            fiveDayWarning = FiveDayNotice.ShouldWarn(merchant?.PlanCode, merchant?.AccessUntil, clock.GetUtcNow()),
            blocked = merchant?.AccessUntil is null || AccessBlock.IsBlocked(merchant.AccessUntil, clock.GetUtcNow()),
            accountEnabled = account.IsOwner || AlertPointAccess.Allows(account, merchant, clock.GetUtcNow()),
            paymentsNeedingReview = account.IsOwner ? await db.BillingOrders.CountAsync(order => order.AdditionalPointId != null && order.PaidAt != null && order.ActivatedAt == null, ct) : 0,
            eventReadyCount = ready.Count, eventDurationHours = ready.FirstOrDefault()?.DurationHours,
            canStartEvent = account.IsOwner && ready.Count > 0 && merchant?.ConnectedAt != null && !(merchant.AccessUntil > clock.GetUtcNow()),
            plans = active.Select(plan => PublicPlan(plan, accounts.Count(point => !point.IsOwner && !point.Revoked && point.RenewWithAdmin && point.AlertPointPosition > AlertPointAccess.Included(plan.Code))))
        });
    }

    private sealed record CheckoutBody(string? ReferralCode, string? PlanCode, Guid? AdditionalPointId);
    private static async Task<IResult> Checkout(CheckoutBody? body, HttpContext http, ICheckout checkout, IPlanCatalog plans,
        IReferralStore referrals, IMerchantStore store, TransfersCheckDb db, TimeProvider clock, ILogger<Program> logger, CancellationToken ct)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner) return Results.Json(new { error = "owner_only" }, statusCode: 403);
        var merchant = await store.FindMerchantAsync(account.TenantId, ct);
        var plan = await plans.FindByCodeAsync(body?.PlanCode ?? merchant?.PlanCode ?? PlanCodes.PackMes, ct);
        if (plan is null || !plan.Active || !IsFixed(plan) || plan.Amount <= 0) return Results.BadRequest(new { error = "invalid_plan" });
        var points = await store.ListAccountsAsync(ct);
        var renew = points.Where(point => !point.IsOwner && !point.Revoked && point.RenewWithAdmin && point.AlertPointPosition > AlertPointAccess.Included(plan.Code)).Select(point => point.Id).ToArray();
        var order = new BillingOrder {
            Id = Guid.NewGuid(), TenantId = account.TenantId, PlanCode = plan.Code, Amount = checked(plan.Amount + renew.Length * AlertPointAccess.AdditionalPrice),
            Currency = plan.Currency, DurationDays = plan.DurationDays, DurationHours = plan.DurationHours,
            RenewPointIds = JsonSerializer.Serialize(renew), CreatedAt = clock.GetUtcNow()
        };
        var title = plan.DisplayName;
        if (body?.AdditionalPointId is { } pointId) {
            var point = points.FirstOrDefault(point => point.Id == pointId && !point.IsOwner && !point.Revoked);
            if (merchant?.AccessUntil is not { } until || until <= clock.GetUtcNow()) return Results.Conflict(new { error = "active_period_required" });
            if (point is null || AlertPointAccess.IsIncluded(point, merchant) || point.ExtraAccessUntil >= until) return Results.BadRequest(new { error = "point_not_payable" });
            order.AdditionalPointId = point.Id; order.TargetAccessUntil = until; order.Amount = AlertPointAccess.AdditionalPrice;
            order.RenewPointIds = "[]"; title = "Punto de Alerta adicional";
        }
        if (!string.IsNullOrWhiteSpace(body?.ReferralCode)) await referrals.AttachCodeAsync(account.TenantId, body.ReferralCode, ct);
        if (order.AdditionalPointId is null && merchant is { ConnectedAt: null, AccessUntil: null }) merchant.PlanCode = plan.Code;
        db.BillingOrders.Add(order); await db.SaveChangesAsync(ct);
        try
        {
            using (MercadoPagoCallScope.Begin(account.TenantId))
                order.CheckoutUrl = await checkout.CreatePaymentLinkAsync($"order:{order.Id:N}", order.Amount, order.Currency, title, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogError(exception, "Checkout link could not be created.");
            return Results.Json(new { error = "checkout_unavailable" }, statusCode: StatusCodes.Status502BadGateway);
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { initPoint = order.CheckoutUrl, amount = order.Amount, planCode = plan.Code, orderId = order.Id, accessUntil = order.TargetAccessUntil });
    }

    private static async Task<IResult> StartEvent(HttpContext http, IMerchantStore store, TransfersCheckDb db, TimeProvider clock, CancellationToken ct)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner) return Results.StatusCode(403);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, account.TenantId, ct);
        var merchant = await store.FindMerchantAsync(account.TenantId, ct);
        if (merchant is null) return Results.NotFound();
        await db.Entry(merchant).ReloadAsync(ct);
        if (merchant.ConnectedAt is null) return Results.Conflict(new { error = "connect_first" });
        if (merchant.AccessUntil > clock.GetUtcNow()) return Results.Conflict(new { error = "already_active", accessUntil = merchant.AccessUntil });
        var order = await db.BillingOrders.Where(order => order.PlanCode == PlanCodes.PackEvento && order.AdditionalPointId == null && order.PaidAt != null && order.ActivatedAt == null).OrderBy(order => order.CreatedAt).FirstOrDefaultAsync(ct);
        if (order is null) return Results.Conflict(new { error = "paid_event_required" });
        var now = clock.GetUtcNow();
        merchant.PlanCode = PlanCodes.PackEvento; merchant.AccessUntil = now.AddHours(order.DurationHours ?? 24); merchant.RefreshNotBefore = null;
        order.ActivatedAt = now; await RenewPoints(db, order, merchant.AccessUntil.Value, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(new { startedAt = now, accessUntil = merchant.AccessUntil });
    }

    private static async Task Lock(TransfersCheckDb db, Guid tenantId, CancellationToken ct)
    {
        var resource = $"TransfersCheck.Billing.{tenantId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Billing lock unavailable', 1;", ct);
    }
    private static async Task RenewPoints(TransfersCheckDb db, BillingOrder order, DateTimeOffset until, CancellationToken ct)
    {
        var ids = JsonSerializer.Deserialize<Guid[]>(order.RenewPointIds) ?? [];
        foreach (var point in await db.LoginAccounts.Where(point => ids.Contains(point.Id) && !point.IsOwner && !point.Revoked).ToListAsync(ct))
            point.ExtraAccessUntil = until;
    }

    private static async Task<IResult> Webhook(HttpContext http, ICheckout checkout, IMerchantStore store, IPlanCatalog plans,
        IReferralStore referrals, ReferralBonusJob job, IOptions<CheckoutOptions> checkoutOptions, IOptions<LoginSessionOptions> sessionOptions, IOptions<CommercialOptions> commercial,
        TimeProvider clock, ITokenProtector protector, TransfersCheckDb db, ITenantContext tenant, CancellationToken ct)
    {
        string? paymentId = http.Request.Query["id"];
        string? signedId = http.Request.Query["data.id"];
        string? signedRef = http.Request.Query["ref"];
        string? refSig = http.Request.Query["sig"];
        if (http.Request.ContentLength is > 0) {
            using var doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id)) paymentId = id.ToString();
        }
        // ADR-022: official x-signature or signed notification_url + provider reconciliation; shared header only in Development/Test.
        if (!WebhookAuthenticator.IsAuthentic(checkoutOptions.Value, http.Request.Headers["X-Webhook-Secret"].ToString(),
                http.Request.Headers["x-signature"].ToString(), http.Request.Headers["x-request-id"].ToString(), signedId ?? paymentId, clock.GetUtcNow(),
                sessionOptions.Value.SigningKey, signedRef, refSig))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(paymentId)) return Results.BadRequest(new { error = "payment_required" });
        if (await store.PaymentAlreadyAppliedAsync(paymentId, ct)) return Results.Ok(new { applied = false });
        var payment = await checkout.ReadApprovedPaymentAsync(paymentId, ct);
        if (payment is null) return Results.Ok(new { applied = false });
        if (!string.IsNullOrWhiteSpace(signedRef) && !string.Equals(payment.Reference, signedRef, StringComparison.Ordinal))
            return Results.Unauthorized();
        BillingOrder? order = null;
        if (payment.Reference.StartsWith("order:", StringComparison.Ordinal) && Guid.TryParse(payment.Reference[6..], out var orderId)) {
            order = await db.BillingOrders.IgnoreQueryFilters().FirstOrDefaultAsync(order => order.Id == orderId, ct);
            if (order is null || order.Amount != payment.Amount || order.Currency != payment.Currency) return Results.Ok(new { applied = false });
        } else {
            // Keep pre-ADR-020 payment links payable during rollout.
            CheckoutOrder? old = null;
            if (Guid.TryParse(payment.Reference, out var oldTenant)) {
                var oldPlan = await plans.FindByCodeAsync(PlanCodes.PackMes, ct);
                if (oldPlan is not null) old = new(oldTenant, PlanCodes.PackMes, oldPlan.Amount, oldPlan.Currency, oldPlan.DurationDays ?? commercial.Value.PaidPeriodDays, null);
            } else try {
                var plain = protector.Unprotect(payment.Reference);
                var parts = plain.StartsWith("checkout:", StringComparison.Ordinal) ? plain[9..].Split('|') : [];
                if (parts.Length == 6) old = new(Guid.Parse(parts[0]), parts[1], int.Parse(parts[2]), parts[3], int.Parse(parts[4]), int.Parse(parts[5]));
            } catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { }
            if (old is null || old.Amount != payment.Amount || old.Currency != payment.Currency) return Results.Ok(new { applied = false });
            order = new BillingOrder { Id = Guid.NewGuid(), TenantId = old.TenantId, PlanCode = old.PlanCode, Amount = old.Amount, Currency = old.Currency, DurationDays = old.DurationDays, DurationHours = old.DurationHours, CreatedAt = clock.GetUtcNow() };
            db.BillingOrders.Add(order);
        }
        tenant.TenantId = order.TenantId;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, order.TenantId, ct);
        if (db.Entry(order).State != EntityState.Added) await db.Entry(order).ReloadAsync(ct);
        if (order.PaidAt != null || await store.PaymentAlreadyAppliedAsync(paymentId, ct)) return Results.Ok(new { applied = false });
        var merchant = await store.FindMerchantAsync(order.TenantId, ct);
        if (merchant is null) return Results.Ok(new { applied = false });
        var now = clock.GetUtcNow(); order.PaidAt = now;
        if (order.AdditionalPointId is { } pointId) {
            var point = await db.LoginAccounts.FirstOrDefaultAsync(point => point.Id == pointId && !point.IsOwner && !point.Revoked, ct);
            if (point != null && order.TargetAccessUntil > now && merchant.AccessUntil > now)
                point.ExtraAccessUntil = point.ExtraAccessUntil > order.TargetAccessUntil ? point.ExtraAccessUntil : order.TargetAccessUntil;
            // A late confirmation is retained as paid without extending a past period (ADR-022).
            order.ActivatedAt = point?.ExtraAccessUntil > now ? now : null;
        } else if (order.PlanCode == PlanCodes.PackMes) {
            var start = merchant.AccessUntil > now ? merchant.AccessUntil.Value : now;
            merchant.AccessUntil = start.AddDays(order.DurationDays ?? 30); merchant.PlanCode = PlanCodes.PackMes;
            order.ActivatedAt = now; await RenewPoints(db, order, merchant.AccessUntil.Value, ct);
            await referrals.QueueGrantFromPackMesPaymentAsync(order.TenantId, now, ct);
        }
        await store.AddProcessedPaymentAsync(new ProcessedPayment { PaymentId = paymentId, TenantId = order.TenantId, AppliedAt = now }, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        if (order.PlanCode == PlanCodes.PackMes && order.AdditionalPointId == null) await job.Execute();
        return Results.Ok(new { applied = true, awaitingEventStart = order.PlanCode == PlanCodes.PackEvento && order.AdditionalPointId == null });
    }
}
