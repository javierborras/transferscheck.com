using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using TransfersCheck.Api.Auth;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Transfers;

public static class TransferEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/transfers", List);
        group.MapGet("/transfers/{id:guid}", Detail);
        group.MapGet("/transfers/live", Live);
        group.MapPost("/transfers/refresh", Refresh);
        group.MapPost("/activity", async (HttpContext http, IMerchantStore store, TimeProvider clock, CancellationToken ct) => {
            var account = (LoginAccount)http.Items["account"]!;
            var merchant = await store.FindMerchantAsync(account.TenantId, ct);
            if (merchant is not null && (merchant.LastInteractionAt is null || clock.GetUtcNow() - merchant.LastInteractionAt > TimeSpan.FromSeconds(10))) {
                merchant.LastInteractionAt = clock.GetUtcNow(); await store.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });
    }

    private static async Task Live(HttpContext http, ILiveListPublisher liveList, IServiceScopeFactory scopes, TimeProvider clock, CancellationToken cancellationToken)
    {
        var original = (LoginAccount)http.Items["account"]!;
        var sessionId = Guid.Parse(http.User.FindFirst("sid")!.Value);
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        await http.Response.Body.FlushAsync(cancellationToken);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var reader = liveList.SubscribeAsync(original.TenantId, lifetime.Token).GetAsyncEnumerator();
        var pending = reader.MoveNextAsync().AsTask();
        async Task<string?> AccessReason() {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId = original.TenantId;
            var store = scope.ServiceProvider.GetRequiredService<IMerchantStore>();
            var account = await store.FindAccountAsync(original.Id, lifetime.Token);
            var session = await store.FindSessionAsync(sessionId, lifetime.Token);
            var merchant = await store.FindMerchantAsync(original.TenantId, lifetime.Token);
            return account is null || account.Revoked || account.LiveSessionId != sessionId || session is null || session.ReplacedAt != null || session.ExpiresAt <= clock.GetUtcNow()
                ? "session-ended" : !AlertPointAccess.Allows(account, merchant, clock.GetUtcNow()) ? "access-paused" : null;
        }
        try {
            while (!lifetime.IsCancellationRequested) {
                var reason = await AccessReason();
                if (reason is not null) {
                    await http.Response.WriteAsync($"event: AccessChanged\ndata: {{\"reason\":\"{reason}\"}}\n\n", cancellationToken);
                    await http.Response.Body.FlushAsync(cancellationToken);
                    break;
                }
                if (await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token)) != pending) {
                    await http.Response.WriteAsync(": heartbeat\n\n", lifetime.Token);
                    await http.Response.Body.FlushAsync(lifetime.Token);
                    continue;
                }
                if (!await pending) break;
                reason = await AccessReason();
                if (reason is not null) {
                    await http.Response.WriteAsync($"event: AccessChanged\ndata: {{\"reason\":\"{reason}\"}}\n\n", cancellationToken);
                    await http.Response.Body.FlushAsync(cancellationToken);
                    break;
                }
                var message = reader.Current;
                var json = System.Text.Json.JsonSerializer.Serialize(new {
                    messageType = LiveTransferArrived.MessageType, id = message.Id, payerName = message.PayerName,
                    amount = message.Amount, occurredAt = message.OccurredAt, status = message.Status, statusLabel = message.StatusLabel
                });
                await http.Response.WriteAsync($"event: {LiveTransferArrived.MessageType}\ndata: {json}\n\n", lifetime.Token);
                await http.Response.Body.FlushAsync(lifetime.Token);
                pending = reader.MoveNextAsync().AsTask();
            }
        } finally {
            lifetime.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    private static async Task<IResult> List(
        Guid? first,
        HttpContext http,
        IMerchantStore store,
        IOptions<SyncOptions> sync,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var take = AlertPointWindow.Take(account.IsOwner, sync.Value.PageSize, sync.Value.AlertPointRecordCount);
        var rows = (await store.ListTransfersAsync(take, cancellationToken)).ToList();
        if (first is { } id)
        {
            var index = rows.FindIndex(row => row.Id == id);
            if (index > 0)
            {
                var chosen = rows[index];
                rows.RemoveAt(index);
                rows.Insert(0, chosen);
            }
        }

        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        var retryAt = new[] { merchant?.RefreshNotBefore, merchant?.RateLimitedUntil }.Where(at => at is not null).Max();
        return Results.Ok(new
        {
            refreshRetrySeconds = Math.Max(0, (int)Math.Ceiling(((retryAt ?? clock.GetUtcNow()) - clock.GetUtcNow()).TotalSeconds)),
            items = rows.Select(MapRow),
            take,
            pollIntervalSeconds = (int)sync.Value.PollInterval.TotalSeconds,
            buttonCooldownSeconds = (int)sync.Value.ButtonCooldown.TotalSeconds
        });
    }

    private static async Task<IResult> Detail(
        Guid id,
        HttpContext http,
        IMerchantStore store,
        IOptions<SyncOptions> sync,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var take = AlertPointWindow.Take(account.IsOwner, sync.Value.PageSize, sync.Value.AlertPointRecordCount);
        var rows = await store.ListTransfersAsync(take, cancellationToken);
        var row = rows.FirstOrDefault(item => item.Id == id);
        if (row is null)
            return Results.NotFound();
        return Results.Ok(MapDetail(row));
    }

    private static async Task<IResult> Refresh(
        HttpContext http,
        TransferSync sync,
        IMerchantStore store,
        IOptions<SyncOptions> options,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var status = await sync.Run(account.TenantId, manual: true, cancellationToken);
        if (status is SyncStatus.Waiting)
        {
            var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
            var retryAt = new[] { merchant?.RefreshNotBefore, merchant?.RateLimitedUntil }.Where(at => at is not null).Max();
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(((retryAt ?? clock.GetUtcNow().AddSeconds(10)) - clock.GetUtcNow()).TotalSeconds));
            return Results.Json(new { error = "waiting", retryAfterSeconds }, statusCode: 429);
        }
        if (status is SyncStatus.NotEntitled)
            return Results.Json(new { error = "not_entitled" }, statusCode: StatusCodes.Status409Conflict);

        if (status is SyncStatus.NotConnected)
            return Results.Conflict(new { error = "not_connected" });
        if (status is SyncStatus.ReconnectRequired)
            return Results.Conflict(new { error = "reconnect_required" });
        var rows = await store.ListTransfersAsync(options.Value.PageSize, cancellationToken);
        return Results.Ok(new { count = rows.Count });
    }

    private static object MapRow(Transfer row) => new
    {
        id = row.Id,
        externalId = row.ExternalId,
        payerName = string.IsNullOrWhiteSpace(row.PayerName) ? "Desconocido" : row.PayerName,
        amount = row.Amount,
        currency = row.Currency,
        occurredAt = row.OccurredAt,
        approvedAt = row.ApprovedAt,
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
    };

    private static object MapDetail(Transfer row) => MapRow(row);
}
