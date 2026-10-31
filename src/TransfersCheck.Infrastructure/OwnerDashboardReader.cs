using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed record OwnerDashboardCounts(
    int TrialsEnding,
    int FiveDayPending,
    int Commerces,
    int Connected,
    int RateLimited,
    int PushDevices,
    int ChecksStale);

public sealed record OwnerClientFilter(
    string? Search,
    string? Plan,
    string? MercadoPago,
    string? Access,
    string? Trial);

public sealed record OwnerClientRow(
    Guid TenantId,
    string? Alias,
    string Email,
    string? PlanCode,
    DateTimeOffset? AccessUntil,
    DateTimeOffset? TrialEndsAt,
    bool Connected,
    int PaidOrdersCount = 0,
    int TotalPaidAmount = 0,
    DateTimeOffset? LastPaymentAt = null,
    string MercadoPago = "sin_vincular",
    string AccessState = "sin",
    string Tone = "ambar",
    string ToneLabel = "Sin vincular",
    bool PendingPayment = false,
    int LoginCount = 0,
    int PushDeviceCount = 0);

public sealed record OwnerDeviceRow(
    Guid Id,
    Guid TenantId,
    string? Alias,
    string Email,
    string AccountLabel,
    bool AlertPoint);

public sealed record OwnerActivityPoint(DateOnly Date, int Signups, int Queries);

public sealed record OwnerActivity(
    int Days,
    bool ShowSignups,
    DateOnly? CallsRecordedFrom,
    IReadOnlyList<OwnerActivityPoint> Points);

public sealed record OwnerBillingRow(
    Guid Id,
    Guid TenantId,
    string? Alias,
    string Email,
    string PlanCode,
    int Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? TargetAccessUntil,
    bool AdditionalPoint);

/// <summary>
/// Cross-tenant reads for the owner dashboard. Collection-account ids and commerce transfers are not selected.
/// </summary>
public static class OwnerDashboardReader
{
    public const int Take = 200;

    public static async Task<OwnerDashboardCounts> CountsAsync(
        TransfersCheckDb db,
        DateTimeOffset now,
        TimeSpan idle,
        CancellationToken cancellationToken)
    {
        var merchants = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Select(m => new MerchantSnap(
                m.Id, m.PlanCode, m.TrialEndsAt, m.AccessUntil, m.FiveDayNoticeSentAt, m.RateLimitedUntil, m.LastTransferCheckAt))
            .ToListAsync(cancellationToken);
        var connected = await db.Connections.IgnoreQueryFilters().AsNoTracking()
            .Where(c => !c.ReconnectRequired)
            .Select(c => c.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var linked = connected.ToHashSet();
        var trialUntil = now.AddDays(FiveDayNotice.DaysBefore);
        return new OwnerDashboardCounts(
            merchants.Count(m => IsTrialEnding(m, now, trialUntil)),
            merchants.Count(m => FiveDayNotice.ShouldWarn(m.PlanCode, m.AccessUntil, now) && m.FiveDayNoticeSentAt is null),
            merchants.Count,
            linked.Count,
            merchants.Count(m => m.RateLimitedUntil > now),
            await db.PushDevices.IgnoreQueryFilters().CountAsync(cancellationToken),
            merchants.Count(m => linked.Contains(m.Id) && (m.LastTransferCheckAt is null || now - m.LastTransferCheckAt > idle)));
    }

    public static Task<IReadOnlyList<OwnerClientRow>> ClientsAsync(TransfersCheckDb db, CancellationToken cancellationToken)
        => ClientsAsync(db, new OwnerClientFilter(null, null, null, null, null), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), cancellationToken);

    public static async Task<IReadOnlyList<OwnerClientRow>> ClientsAsync(
        TransfersCheckDb db,
        OwnerClientFilter filter,
        DateTimeOffset now,
        TimeSpan idle,
        CancellationToken cancellationToken)
    {
        // Project the columns this list shows. A full entity load selects every Merchant
        // column, so one column missing from an older production database (EnsureSchema
        // stays off) fails this read while Inicio's narrower projection still succeeds.
        var merchants = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Select(m => new MerchantListSnap(
                m.Id, m.Alias, m.PlanCode, m.AccessUntil, m.TrialEndsAt, m.CreatedAt, m.RateLimitedUntil, m.LastTransferCheckAt))
            .ToListAsync(cancellationToken);
        if (merchants.Count == 0)
            return [];
        var ids = merchants.Select(m => m.Id).ToArray();
        var emails = await AdminEmailsAsync(db, ids, cancellationToken);
        var links = await LinkStatesAsync(db, cancellationToken);
        var matched = merchants
            .Where(m => Matches(m, emails.GetValueOrDefault(m.Id) ?? "", links.GetValueOrDefault(m.Id), filter, now, idle))
            .OrderByDescending(m => m.CreatedAt)
            .Take(Take)
            .ToList();
        return await MaterializeAsync(db, matched, emails, links, now, cancellationToken);
    }

    public static async Task<OwnerClientRow?> ClientAsync(
        TransfersCheckDb db,
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Id == tenantId)
            .Select(m => new MerchantListSnap(
                m.Id, m.Alias, m.PlanCode, m.AccessUntil, m.TrialEndsAt, m.CreatedAt, m.RateLimitedUntil, m.LastTransferCheckAt))
            .FirstOrDefaultAsync(cancellationToken);
        if (merchant is null)
            return null;
        var emails = await AdminEmailsAsync(db, [tenantId], cancellationToken);
        var links = await LinkStatesAsync(db, cancellationToken);
        var rows = await MaterializeAsync(db, [merchant], emails, links, now, cancellationToken);
        return rows[0];
    }

    public static async Task<IReadOnlyList<OwnerDeviceRow>> DevicesAsync(TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var devices = await db.PushDevices.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(d => d.Id)
            .Take(Take)
            .Select(d => new { d.Id, d.TenantId, d.AccountId })
            .ToListAsync(cancellationToken);
        if (devices.Count == 0)
            return [];
        var tenantIds = devices.Select(d => d.TenantId).Distinct().ToArray();
        var accountIds = devices.Select(d => d.AccountId).Distinct().ToArray();
        var emails = await AdminEmailsAsync(db, tenantIds, cancellationToken);
        var aliases = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Where(m => tenantIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Alias })
            .ToDictionaryAsync(m => m.Id, m => m.Alias, cancellationToken);
        var accounts = await db.LoginAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.IsOwner, a.UserName, a.Revoked })
            .ToDictionaryAsync(a => a.Id, cancellationToken);
        return devices.Select(device =>
        {
            accounts.TryGetValue(device.AccountId, out var account);
            var alertPoint = account is { IsOwner: false };
            var label = account is null
                ? "Cuenta"
                : account.IsOwner
                    ? "Administrador"
                    : string.IsNullOrWhiteSpace(account.UserName) ? "Punto de Alerta" : account.UserName;
            if (account is { Revoked: true })
                label += " (revocada)";
            return new OwnerDeviceRow(
                device.Id,
                device.TenantId,
                aliases.GetValueOrDefault(device.TenantId),
                emails.GetValueOrDefault(device.TenantId) ?? "",
                label,
                alertPoint);
        }).ToList();
    }

    public static async Task<OwnerActivity?> ActivityAsync(
        TransfersCheckDb db,
        int days,
        Guid? tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (tenantId is { } id && !await db.Merchants.IgnoreQueryFilters().AnyAsync(m => m.Id == id, cancellationToken))
            return null;

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var start = today.AddDays(-(days - 1));
        var startInstant = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endInstant = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var signupsQuery = db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.CreatedAt >= startInstant && m.CreatedAt < endInstant);
        var callsQuery = db.MercadoPagoApiCalls.AsNoTracking()
            .Where(c => c.OccurredAt >= startInstant && c.OccurredAt < endInstant);
        var firstQuery = db.MercadoPagoApiCalls.AsNoTracking().AsQueryable();
        if (tenantId is { } tenant)
        {
            signupsQuery = signupsQuery.Where(m => m.Id == tenant);
            callsQuery = callsQuery.Where(c => c.TenantId == tenant);
            firstQuery = firstQuery.Where(c => c.TenantId == tenant);
        }

        var signups = Bucket(await signupsQuery.Select(m => m.CreatedAt).ToListAsync(cancellationToken));
        var queries = Bucket(await callsQuery.Select(c => c.OccurredAt).ToListAsync(cancellationToken));
        var first = await firstQuery.OrderBy(c => c.OccurredAt).Select(c => (DateTimeOffset?)c.OccurredAt).FirstOrDefaultAsync(cancellationToken);
        var points = new List<OwnerActivityPoint>(days);
        for (var day = start; day <= today; day = day.AddDays(1))
            points.Add(new OwnerActivityPoint(day, signups.GetValueOrDefault(day), queries.GetValueOrDefault(day)));

        var showSignups = tenantId is null || signups.Values.Any(count => count > 0);
        return new OwnerActivity(
            days,
            showSignups,
            first is null ? null : DateOnly.FromDateTime(first.Value.UtcDateTime),
            points);
    }

    private static async Task<IReadOnlyList<OwnerClientRow>> MaterializeAsync(
        TransfersCheckDb db,
        IReadOnlyList<MerchantListSnap> merchants,
        IReadOnlyDictionary<Guid, string> emails,
        IReadOnlyDictionary<Guid, LinkState> links,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (merchants.Count == 0)
            return [];
        var ids = merchants.Select(m => m.Id).ToArray();
        var orders = await db.BillingOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => ids.Contains(o.TenantId))
            .Select(o => new { o.TenantId, o.Amount, o.PaidAt })
            .ToListAsync(cancellationToken);
        var paidByTenant = orders.Where(o => o.PaidAt != null).GroupBy(o => o.TenantId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(x => x.Amount), Last: g.Max(x => x.PaidAt)));
        var pending = orders.Where(o => o.PaidAt is null).Select(o => o.TenantId).ToHashSet();
        var logins = await db.LoginAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => ids.Contains(a.TenantId) && !a.Revoked)
            .GroupBy(a => a.TenantId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var pushes = await db.PushDevices.IgnoreQueryFilters().AsNoTracking()
            .Where(d => ids.Contains(d.TenantId))
            .GroupBy(d => d.TenantId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return merchants.Select(m =>
        {
            var stats = paidByTenant.GetValueOrDefault(m.Id);
            var link = links.GetValueOrDefault(m.Id);
            var mercadoPago = OwnerClientStatus.MercadoPago(link.Linked, link.Reconnect);
            var access = OwnerClientStatus.Access(m.AccessUntil, now);
            var pendingPayment = pending.Contains(m.Id);
            var tone = OwnerClientStatus.Tone(mercadoPago, access, pendingPayment, OwnerClientStatus.TrialEnding(m.TrialEndsAt, now));
            return new OwnerClientRow(
                m.Id,
                m.Alias,
                emails.GetValueOrDefault(m.Id) ?? "",
                m.PlanCode,
                m.AccessUntil,
                m.TrialEndsAt,
                link.Linked,
                stats.Count,
                stats.Total,
                stats.Last,
                mercadoPago,
                access,
                tone.Tone,
                tone.Label,
                pendingPayment,
                logins.GetValueOrDefault(m.Id),
                pushes.GetValueOrDefault(m.Id));
        }).ToList();
    }

    private static bool Matches(
        MerchantListSnap merchant,
        string email,
        LinkState link,
        OwnerClientFilter filter,
        DateTimeOffset now,
        TimeSpan idle)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var needle = filter.Search.Trim().TrimStart('#');
            var aliasHit = merchant.Alias?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
            var emailHit = email.Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (!aliasHit && !emailHit)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Plan)
            && !string.Equals(merchant.PlanCode, filter.Plan, StringComparison.OrdinalIgnoreCase))
            return false;

        var mercadoPago = OwnerClientStatus.MercadoPago(link.Linked, link.Reconnect);
        if (!string.IsNullOrWhiteSpace(filter.MercadoPago))
        {
            var wanted = filter.MercadoPago.Trim().ToLowerInvariant();
            var hit = wanted switch
            {
                "vinculado" => mercadoPago == "vinculado",
                "sin_vincular" => mercadoPago == "sin_vincular",
                "reconectar" => mercadoPago == "reconectar",
                "espera" => merchant.RateLimitedUntil > now,
                "demoradas" => mercadoPago == "vinculado"
                    && (merchant.LastTransferCheckAt is null || now - merchant.LastTransferCheckAt > idle),
                _ => true
            };
            if (!hit)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Access)
            && !string.Equals(OwnerClientStatus.Access(merchant.AccessUntil, now), filter.Access.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(filter.Trial))
        {
            var active = OwnerClientStatus.TrialActive(merchant.TrialEndsAt, now);
            var wanted = filter.Trial.Trim().ToLowerInvariant();
            if (wanted == "vigente" && !active)
                return false;
            if (wanted == "no" && active)
                return false;
        }

        return true;
    }

    private static async Task<Dictionary<Guid, LinkState>> LinkStatesAsync(TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var rows = await db.Connections.IgnoreQueryFilters().AsNoTracking()
            .Select(c => new { c.TenantId, c.ReconnectRequired })
            .ToListAsync(cancellationToken);
        var links = new Dictionary<Guid, LinkState>();
        foreach (var row in rows)
        {
            links.TryGetValue(row.TenantId, out var state);
            links[row.TenantId] = new LinkState(state.Linked || !row.ReconnectRequired, state.Reconnect || row.ReconnectRequired);
        }
        return links;
    }

    private static Dictionary<DateOnly, int> Bucket(IEnumerable<DateTimeOffset> instants)
    {
        var buckets = new Dictionary<DateOnly, int>();
        foreach (var instant in instants)
        {
            var day = DateOnly.FromDateTime(instant.UtcDateTime);
            buckets[day] = buckets.GetValueOrDefault(day) + 1;
        }
        return buckets;
    }

    public static async Task<IReadOnlyList<OwnerBillingRow>> BillingAsync(
        TransfersCheckDb db,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var query = db.BillingOrders.IgnoreQueryFilters().AsNoTracking();
        if (tenantId is { } id)
            query = query.Where(o => o.TenantId == id);
        var orders = await query.OrderByDescending(o => o.CreatedAt).Take(Take).ToListAsync(cancellationToken);
        if (orders.Count == 0)
            return [];
        var ids = orders.Select(o => o.TenantId).Distinct().ToArray();
        var emails = await AdminEmailsAsync(db, ids, cancellationToken);
        var aliases = await db.Merchants.IgnoreQueryFilters().AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Alias })
            .ToDictionaryAsync(m => m.Id, m => m.Alias, cancellationToken);
        return orders.Select(o => new OwnerBillingRow(
            o.Id,
            o.TenantId,
            aliases.GetValueOrDefault(o.TenantId),
            emails.GetValueOrDefault(o.TenantId) ?? "",
            o.PlanCode,
            o.Amount,
            o.Currency,
            o.CreatedAt,
            o.PaidAt,
            o.ActivatedAt,
            o.TargetAccessUntil,
            o.AdditionalPointId is not null)).ToList();
    }

    private static async Task<Dictionary<Guid, string>> AdminEmailsAsync(
        TransfersCheckDb db,
        Guid[] tenantIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.LoginAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.IsOwner && tenantIds.Contains(a.TenantId))
            .Select(a => new { a.TenantId, a.Email })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(a => a.TenantId).ToDictionary(g => g.Key, g => g.First().Email);
    }

    private static bool IsTrialEnding(MerchantSnap merchant, DateTimeOffset now, DateTimeOffset until)
        => string.Equals(merchant.PlanCode, PlanCodes.PackMes, StringComparison.OrdinalIgnoreCase)
            && merchant.TrialEndsAt is { } trial
            && trial > now
            && trial <= until;

    private readonly record struct LinkState(bool Linked, bool Reconnect);

    private sealed record MerchantListSnap(
        Guid Id,
        string? Alias,
        string? PlanCode,
        DateTimeOffset? AccessUntil,
        DateTimeOffset? TrialEndsAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset? RateLimitedUntil,
        DateTimeOffset? LastTransferCheckAt);

    private sealed record MerchantSnap(
        Guid Id,
        string? PlanCode,
        DateTimeOffset? TrialEndsAt,
        DateTimeOffset? AccessUntil,
        DateTimeOffset? FiveDayNoticeSentAt,
        DateTimeOffset? RateLimitedUntil,
        DateTimeOffset? LastTransferCheckAt);
}
