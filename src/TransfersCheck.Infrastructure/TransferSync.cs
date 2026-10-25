using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public enum SyncStatus
{
    Ok,
    Waiting,
    NotEntitled,
    NotConnected,
    QuotaWait
}

public sealed class TransferSync(
    ITenantContext tenant,
    IMerchantStore store,
    ITransferSource source,
    IMerchantAuthorizer authorizer,
    IEntitlement entitlement,
    ITokenProtector protector,
    IPushSender push,
    ILiveListPublisher liveList,
    IConsultaCounter consultas,
    IOptions<SyncOptions> syncOptions,
    IOptions<CommercialOptions> commercial,
    TimeProvider clock)
{
    public async Task<SyncStatus> Run(Guid tenantId, bool manual, CancellationToken cancellationToken)
    {
        tenant.TenantId = tenantId;
        var merchant = await store.FindMerchantAsync(tenantId, cancellationToken);
        if (merchant is null)
            return SyncStatus.NotConnected;

        var now = clock.GetUtcNow();
        if (!entitlement.AllowsSyncAndPush(merchant.AccessUntil, now))
            return SyncStatus.NotEntitled;
        if (merchant.RateLimitedUntil is { } wait && wait > now)
            return SyncStatus.Waiting;
        if (manual && merchant.RefreshNotBefore is { } next && next > now)
            return SyncStatus.Waiting;

        // Story 10.1: at the weekly wall, queries and alerts wait — poll must not sync or push either.
        if (merchant.ConnectedAt is { } link)
        {
            var (periodStart, periodEnd) = WeeklyQueryQuota.Period(link, now);
            var used = await consultas.CountAsync(tenantId, periodStart, periodEnd, cancellationToken);
            if (WeeklyQueryQuota.Evaluate(used, commercial.Value.WeeklyQueryCap, commercial.Value.WeeklyQueryWarningGap) is QuotaLevel.Wait)
                return SyncStatus.QuotaWait;
        }

        var connection = await store.FindConnectionAsync(cancellationToken);
        if (connection is null)
            return SyncStatus.NotConnected;

        var access = protector.Unprotect(connection.AccessTokenProtected);
        var refresh = protector.Unprotect(connection.RefreshTokenProtected);
        if (connection.AccessTokenExpiresAt <= now)
        {
            var refreshed = await authorizer.RefreshAsync(refresh, cancellationToken);
            access = refreshed.AccessToken;
            refresh = refreshed.RefreshToken;
            connection.AccessTokenProtected = protector.Protect(access);
            connection.RefreshTokenProtected = protector.Protect(refresh);
            connection.AccessTokenExpiresAt = refreshed.ExpiresAt;
            await store.SaveChangesAsync(cancellationToken);
        }

        IReadOnlyList<TransferChange> changes;
        try
        {
            var read = await source.ReadIncomingAsync(
                new TokenSet(access, refresh, connection.AccessTokenExpiresAt, connection.ExternalAccountId),
                syncOptions.Value.PageSize,
                cancellationToken);
            var allowed = await store.ListStoredTypeCodesAsync(cancellationToken);
            var incoming = read.Where(item => allowed.Contains(item.TypeCode)).ToArray();
            changes = await store.UpsertTransfersAsync(source.SourceCode, incoming, cancellationToken);
        }
        catch (RateLimitedException)
        {
            merchant.RateLimitedUntil = now.Add(syncOptions.Value.PollInterval);
            await store.SaveChangesAsync(cancellationToken);
            return SyncStatus.Waiting;
        }

        if (manual)
        {
            merchant.RefreshNotBefore = now.Add(syncOptions.Value.ButtonCooldown);
            await store.SaveChangesAsync(cancellationToken);
        }

        foreach (var change in changes)
        {
            await liveList.PublishAsync(
                tenantId,
                new LiveTransferArrived(
                    change.TransferId,
                    string.IsNullOrWhiteSpace(change.PayerName) ? "Desconocido" : change.PayerName,
                    change.Amount,
                    change.OccurredAt,
                    change.Status,
                    StatusWords.Label(change.Status)),
                cancellationToken);
        }

        if (entitlement.AllowsSyncAndPush(merchant.AccessUntil, now))
        {
            var devices = await store.ListApprovedPushDevicesAsync(cancellationToken);
            foreach (var change in changes.Where(change => change.NewlyArrived))
            {
                foreach (var device in devices)
                    await push.SendArrivedAsync(device.Token, change.TransferId, change.PayerName, change.Amount, "ARS", cancellationToken);
            }
        }

        return SyncStatus.Ok;
    }
}
