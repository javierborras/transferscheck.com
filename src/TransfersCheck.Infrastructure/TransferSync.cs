using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public enum SyncStatus
{
    Ok,
    Waiting,
    NotEntitled,
    NotConnected
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
    IOptions<SyncOptions> syncOptions,
    TimeProvider clock,
    TransfersCheckDb db,
    ILogger<TransferSync> logger)
{
    public async Task<SyncStatus> Run(Guid tenantId, bool manual, CancellationToken cancellationToken)
    {
        tenant.TenantId = tenantId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var resource = $"TransfersCheck.Refresh.{tenantId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Refresh lock unavailable', 1;", cancellationToken);
        var merchant = await store.FindMerchantAsync(tenantId, cancellationToken);
        if (merchant is null)
            return SyncStatus.NotConnected;

        await db.Entry(merchant).ReloadAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (!entitlement.AllowsSyncAndPush(merchant.AccessUntil, now))
            return SyncStatus.NotEntitled;
        if (merchant.RateLimitedUntil is { } wait && wait > now)
            return SyncStatus.Waiting;
        if (merchant.RefreshNotBefore is { } next && next > now)
            return SyncStatus.Waiting;

        if (!manual && merchant.LastTransferCheckAt is { } last && now - last < PollingCadence.Interval(merchant.LastInteractionAt, now, syncOptions.Value))
            return SyncStatus.Waiting;

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

        merchant.LastTransferCheckAt = now;
        merchant.RefreshNotBefore = now.Add(syncOptions.Value.ButtonCooldown);
        if (manual) {
            merchant.LastInteractionAt = now;
            merchant.RefreshNotBefore = now.Add(syncOptions.Value.ButtonCooldown);
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
            merchant.RateLimitedUntil = now.Add(syncOptions.Value.IdlePollInterval);
            await store.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SyncStatus.Waiting;
        }
        catch (HttpRequestException ex) {
            logger.LogWarning(ex, "Transfer provider unavailable; retaining technical cooldown.");
            await store.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SyncStatus.Waiting;
        }

        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
            var accounts = (await store.ListAccountsAsync(cancellationToken)).ToDictionary(account => account.Id);
            devices = devices.Where(device => accounts.TryGetValue(device.AccountId, out var account) && AlertPointAccess.Allows(account, merchant, now)).ToArray();
            foreach (var change in changes.Where(change => change.NewlyArrived))
            {
                foreach (var device in devices.DistinctBy(device => device.Token))
                {
                    try { await push.SendArrivedAsync(device.Token, change.TransferId, change.PayerName, change.Amount, "ARS", cancellationToken); }
                    catch (InvalidPushTokenException) {
                        devices = devices.Where(item => item.Id != device.Id).ToArray();
                        db.PushDevices.Remove(device);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Push delivery failed for device {DeviceId}; transfer remains recorded.", device.Id);
                    }
                }
            }
        }

        return SyncStatus.Ok;
    }
}
