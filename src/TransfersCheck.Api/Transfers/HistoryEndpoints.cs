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
        IMerchantAuthorizer authorizer, TransfersCheckDb db, IOptions<SyncOptions> sync, TimeProvider clock, CancellationToken ct)
    {
        var account = (LoginAccount)http.Items["account"]!;
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
        merchant.RefreshNotBefore = now.Add(sync.Value.ButtonCooldown); merchant.LastInteractionAt = now;
        var access = protector.Unprotect(connection.AccessTokenProtected);
        if (connection.AccessTokenExpiresAt <= now) {
            var token = await authorizer.RefreshAsync(protector.Unprotect(connection.RefreshTokenProtected), ct);
            connection.AccessTokenProtected = protector.Protect(token.AccessToken); connection.RefreshTokenProtected = protector.Protect(token.RefreshToken); connection.AccessTokenExpiresAt = token.ExpiresAt;
            access = token.AccessToken;
        }
        try {
            var page = await source.QueryAsync(new TokenSet(access, protector.Unprotect(connection.RefreshTokenProtected), connection.AccessTokenExpiresAt, connection.ExternalAccountId), from, to, offset ?? 0, 20, ct);
            var allowed = await store.ListStoredTypeCodesAsync(ct);
            var items = page.Items.Where(row => allowed.Contains(row.TypeCode)).Select(row => new {
                id = row.ExternalId, occurredAt = row.OccurredAt, payerName = TransferHistory.DisplayPayer(row.PayerName), amount = row.Amount,
                status = row.Status, statusLabel = StatusWords.Label(row.Status)
            }).ToArray();
            await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { title = "Movimientos de tu cuenta", columns = new[] { "Fecha", "Pagador", "Monto", "Estado" }, items,
                nextOffset = page.NextOffset, offset = offset ?? 0, buttonCooldownSeconds = (int)sync.Value.ButtonCooldown.TotalSeconds,
                totals = new { visible = items.Where(row => row.status == "approved").Sum(row => row.amount) } });
        } catch (RateLimitedException) {
            merchant.RateLimitedUntil = now.Add(sync.Value.IdlePollInterval);
            await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Json(new { error = "waiting", retryAfterSeconds = (int)sync.Value.IdlePollInterval.TotalSeconds }, statusCode: 429);
        } catch (HttpRequestException) {
            await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.Json(new { error = "provider_unavailable", retryAfterSeconds = (int)sync.Value.ButtonCooldown.TotalSeconds }, statusCode: 502);
        }
    }
}
