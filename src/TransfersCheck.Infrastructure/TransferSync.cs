using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public enum SyncStatus
{
    Ok,
    Waiting,
    NotEntitled,
    NotConnected,
    ReconnectRequired
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
        using var mercadoPagoScope = MercadoPagoCallScope.Begin(tenantId);
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
        if (connection.ReconnectRequired)
            return SyncStatus.ReconnectRequired;

        string access;
        string refresh;
        try
        {
            access = protector.Unprotect(connection.AccessTokenProtected);
            refresh = protector.Unprotect(connection.RefreshTokenProtected);
        }
        catch (CryptographicException)
        {
            // The key ring changed, or these tokens were sealed by another one. That is a dead link, not a provider outage.
            return await MarkReconnectAsync(connection, merchant, tenantId, transaction, cancellationToken, unreadableTokens: true);
        }
        IReadOnlyList<TransferChange> changes;
        try
        {
            if (connection.AccessTokenExpiresAt <= now)
            {
                if (!await TryRefreshAsync(connection, refresh, cancellationToken))
                    return await MarkReconnectAsync(connection, merchant, tenantId, transaction, cancellationToken);
                access = protector.Unprotect(connection.AccessTokenProtected);
                refresh = protector.Unprotect(connection.RefreshTokenProtected);
            }

            merchant.LastTransferCheckAt = now;
            merchant.RefreshNotBefore = now.Add(syncOptions.Value.ButtonCooldown);
            if (manual)
                merchant.LastInteractionAt = now;

            IReadOnlyList<IncomingTransfer> read;
            try
            {
                read = await ReadIncomingAsync(connection, access, refresh, cancellationToken);
            }
            catch (HttpRequestException ex) when (MerchantLink.IsUnauthorized(ex))
            {
                // The stored access token was still inside our expiry window, but Mercado Pago rejected it.
                // Refresh once. A second 401, or a refresh the provider refuses, means the link is dead.
                if (!await TryRefreshAsync(connection, refresh, cancellationToken))
                    return await MarkReconnectAsync(connection, merchant, tenantId, transaction, cancellationToken);
                access = protector.Unprotect(connection.AccessTokenProtected);
                refresh = protector.Unprotect(connection.RefreshTokenProtected);
                try
                {
                    read = await ReadIncomingAsync(connection, access, refresh, cancellationToken);
                }
                catch (HttpRequestException retry) when (MerchantLink.IsUnauthorized(retry))
                {
                    return await MarkReconnectAsync(connection, merchant, tenantId, transaction, cancellationToken);
                }
            }

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
        catch (MerchantAuthorizationRejectedException)
        {
            return await MarkReconnectAsync(connection, merchant, tenantId, transaction, cancellationToken);
        }
        catch (HttpRequestException ex) {
            if (merchant.RefreshNotBefore is null || merchant.RefreshNotBefore <= now)
                merchant.RefreshNotBefore = now.Add(syncOptions.Value.ButtonCooldown);
            logger.LogError(ex, "Transfer provider unavailable; retaining technical cooldown.");
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

    private Task<IReadOnlyList<IncomingTransfer>> ReadIncomingAsync(
        MerchantConnection connection, string access, string refresh, CancellationToken cancellationToken)
        => source.ReadIncomingAsync(
            new TokenSet(access, refresh, connection.AccessTokenExpiresAt, connection.ExternalAccountId),
            syncOptions.Value.PageSize,
            cancellationToken);

    private async Task<bool> TryRefreshAsync(MerchantConnection connection, string refresh, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refresh))
            return false;
        try
        {
            var refreshed = await authorizer.RefreshAsync(refresh, cancellationToken);
            MerchantLink.StoreTokens(connection, refreshed, refresh, protector);
            return true;
        }
        catch (MerchantAuthorizationRejectedException)
        {
            return false;
        }
    }

    private async Task<SyncStatus> MarkReconnectAsync(
        MerchantConnection connection, Merchant merchant, Guid tenantId, IDbContextTransaction transaction, CancellationToken cancellationToken, bool unreadableTokens = false)
    {
        MerchantLink.RequireReconnect(connection, merchant);
        if (unreadableTokens)
            logger.LogWarning("Stored Mercado Pago tokens for tenant {TenantId} cannot be decrypted; the commerce must reconnect.", tenantId);
        else
            logger.LogWarning("Mercado Pago authorization was rejected for tenant {TenantId}; the commerce must reconnect.", tenantId);
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SyncStatus.ReconnectRequired;
    }
}
