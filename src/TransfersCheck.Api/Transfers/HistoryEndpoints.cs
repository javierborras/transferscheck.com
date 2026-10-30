using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransfersCheck.Api.Auth;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Transfers;

public static class HistoryEndpoints
{
    public static void Map(WebApplication app)
        => app.MapGet("/api/history", Query).AddEndpointFilter<LiveSessionFilter>();

    private static async Task<IResult> Query(DateTimeOffset? from, DateTimeOffset? to, int? offset,
        HttpContext http, IMerchantStore store, ITransferQuerySource source, ITokenProtector protector,
        IMerchantAuthorizer authorizer, TransfersCheckDb db, IOptions<SyncOptions> sync, TimeProvider clock,
        ILogger<Program> logger, CancellationToken ct)
    {
        var account = (LoginAccount)http.Items["account"]!;
        using var mercadoPagoScope = MercadoPagoCallScope.Begin(account.TenantId);
        if (!account.IsOwner) return Results.StatusCode(403);
        if (offset is < 0 or > 100000 || from > to) return Results.BadRequest(new { error = "invalid_range" });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var resource = $"TransfersCheck.Refresh.{account.TenantId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Refresh lock unavailable', 1;", ct);
        var merchant = await store.FindMerchantAsync(account.TenantId, ct);
        if (merchant is null) return Results.NotFound();
        await db.Entry(merchant).ReloadAsync(ct);
        var now = clock.GetUtcNow();
        if (!(merchant.AccessUntil > now)) return Results.Conflict(new { error = "not_entitled" });
        var wait = new[] { merchant.RefreshNotBefore, merchant.RateLimitedUntil }.Where(at => at > now).Max();
        if (wait > now) return Results.Json(new { error = "waiting", retryAfterSeconds = (int)Math.Ceiling((wait.Value - now).TotalSeconds) }, statusCode: 429);
        var connection = await store.FindConnectionAsync(ct);
        if (connection is null) return Results.Conflict(new { error = "connect_first" });
        if (connection.ReconnectRequired) return Results.Conflict(new { error = "reconnect_required" });
        merchant.RefreshNotBefore = now.Add(sync.Value.ButtonCooldown); merchant.LastInteractionAt = now;
        var access = protector.Unprotect(connection.AccessTokenProtected);
        var refresh = protector.Unprotect(connection.RefreshTokenProtected);
        var refreshed = false;
        if (connection.AccessTokenExpiresAt <= now) {
            if (!await TryRefresh(connection, refresh, authorizer, protector, ct))
                return await RejectLink(connection, merchant, store, transaction, ct);
            refreshed = true;
            access = protector.Unprotect(connection.AccessTokenProtected);
            refresh = protector.Unprotect(connection.RefreshTokenProtected);
        }
        try {
            TransferPage page;
            try
            {
                page = await source.QueryAsync(new TokenSet(access, refresh, connection.AccessTokenExpiresAt, connection.ExternalAccountId), from, to, offset ?? 0, 20, ct);
            }
            catch (HttpRequestException unauthorized) when (MerchantLink.IsUnauthorized(unauthorized))
            {
                if (refreshed || !await TryRefresh(connection, refresh, authorizer, protector, ct))
                    return await RejectLink(connection, merchant, store, transaction, ct);
                access = protector.Unprotect(connection.AccessTokenProtected);
                refresh = protector.Unprotect(connection.RefreshTokenProtected);
                try
                {
                    page = await source.QueryAsync(new TokenSet(access, refresh, connection.AccessTokenExpiresAt, connection.ExternalAccountId), from, to, offset ?? 0, 20, ct);
                }
                catch (HttpRequestException retry) when (MerchantLink.IsUnauthorized(retry))
                {
                    return await RejectLink(connection, merchant, store, transaction, ct);
                }
            }
            var allowed = await store.ListStoredTypeCodesAsync(ct);
            var items = page.Items.Where(row => allowed.Contains(row.TypeCode)).Select(row => new {
                id = row.ExternalId, externalId = row.ExternalId, occurredAt = row.OccurredAt, approvedAt = row.ApprovedAt,
                payerName = TransferHistory.DisplayPayer(row.PayerName), amount = row.Amount, currency = row.Currency,
                status = row.Status, statusLabel = StatusWords.Label(row.Status),
                statusDetail = row.StatusDetail, statusDetailLabel = StatusWords.DetailLabel(row.StatusDetail),
                typeCode = row.TypeCode, operationType = row.OperationType, paymentMethodId = row.PaymentMethodId,
                operationLabel = StatusWords.OperationLabel(row.OperationType, row.PaymentMethodId, row.TypeCode),
                coelsaId = row.CoelsaId, bankTransferId = row.BankTransferId,
                payerDocument = row.PayerDocument, payerEmail = row.PayerEmail,
                originBank = row.OriginBank, originAccount = row.OriginAccount, description = row.Description
            }).ToArray();
            await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { title = "Movimientos de tu cuenta", columns = new[] { "Fecha", "Pagador", "Monto", "Estado" }, items,
                nextOffset = page.NextOffset, offset = offset ?? 0, buttonCooldownSeconds = (int)sync.Value.ButtonCooldown.TotalSeconds,
                totals = new { visible = items.Where(row => row.status == "approved").Sum(row => row.amount) } });
        } catch (RateLimitedException) {
            merchant.RateLimitedUntil = now.Add(sync.Value.IdlePollInterval);
            await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Json(new { error = "waiting", retryAfterSeconds = (int)sync.Value.IdlePollInterval.TotalSeconds }, statusCode: 429);
        } catch (MerchantAuthorizationRejectedException) {
            return await RejectLink(connection, merchant, store, transaction, ct);
        } catch (HttpRequestException exception) {
            logger.LogError(exception, "Transfer history provider failed.");
            await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Json(new { error = "provider_unavailable", retryAfterSeconds = (int)sync.Value.ButtonCooldown.TotalSeconds }, statusCode: 502);
        }
    }

    private static async Task<bool> TryRefresh(MerchantConnection connection, string refresh, IMerchantAuthorizer authorizer, ITokenProtector protector, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refresh))
            return false;
        try
        {
            var token = await authorizer.RefreshAsync(refresh, ct);
            MerchantLink.StoreTokens(connection, token, refresh, protector);
            return true;
        }
        catch (MerchantAuthorizationRejectedException)
        {
            return false;
        }
    }

    private static async Task<IResult> RejectLink(MerchantConnection connection, Merchant merchant, IMerchantStore store, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        MerchantLink.RequireReconnect(connection, merchant);
        await store.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Conflict(new { error = "reconnect_required" });
    }
}
