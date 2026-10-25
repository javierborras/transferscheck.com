using Microsoft.Extensions.Options;
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
    }

    private static async Task Live(
        HttpContext http,
        ILiveListPublisher liveList,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        await http.Response.Body.FlushAsync(cancellationToken);
        await foreach (var message in liveList.SubscribeAsync(account.TenantId, cancellationToken))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                messageType = LiveTransferArrived.MessageType,
                id = message.Id,
                payerName = message.PayerName,
                amount = message.Amount,
                occurredAt = message.OccurredAt,
                status = message.Status,
                statusLabel = message.StatusLabel
            });
            await http.Response.WriteAsync($"event: {LiveTransferArrived.MessageType}\ndata: {json}\n\n", cancellationToken);
            await http.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static async Task<IResult> List(
        Guid? first,
        HttpContext http,
        IMerchantStore store,
        IOptions<SyncOptions> sync,
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

        return Results.Ok(new
        {
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
        return Results.Ok(MapRow(row));
    }

    private static async Task<IResult> Refresh(
        HttpContext http,
        TransferSync sync,
        IMerchantStore store,
        IConsultaCounter consultas,
        IOptions<SyncOptions> options,
        IOptions<CommercialOptions> commercial,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        if (!account.IsOwner)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var merchant = await store.FindMerchantAsync(account.TenantId, cancellationToken);
        var now = clock.GetUtcNow();
        if (merchant?.ConnectedAt is { } link)
        {
            var (periodStart, periodEnd) = WeeklyQueryQuota.Period(link, now);
            var used = await consultas.CountAsync(account.TenantId, periodStart, periodEnd, cancellationToken);
            if (WeeklyQueryQuota.Evaluate(used, commercial.Value.WeeklyQueryCap, commercial.Value.WeeklyQueryWarningGap) is QuotaLevel.Wait)
                return Results.Json(new { error = "quota_wait", used, cap = commercial.Value.WeeklyQueryCap }, statusCode: StatusCodes.Status429TooManyRequests);
            await consultas.IncrementOnDemandAsync(account.TenantId, now, periodStart, cancellationToken);
        }

        var status = await sync.Run(account.TenantId, manual: true, cancellationToken);
        if (status is SyncStatus.QuotaWait)
            return Results.Json(new { error = "quota_wait", cap = commercial.Value.WeeklyQueryCap }, statusCode: StatusCodes.Status429TooManyRequests);
        if (status is SyncStatus.Waiting)
            return Results.Json(new { error = "waiting" }, statusCode: StatusCodes.Status429TooManyRequests);
        if (status is SyncStatus.NotEntitled)
            return Results.Json(new { error = "not_entitled" }, statusCode: StatusCodes.Status409Conflict);

        var rows = await store.ListTransfersAsync(options.Value.PageSize, cancellationToken);
        return Results.Ok(new { count = rows.Count });
    }

    private static object MapRow(Transfer row) => new
    {
        id = row.Id,
        payerName = string.IsNullOrWhiteSpace(row.PayerName) ? "Desconocido" : row.PayerName,
        amount = row.Amount,
        occurredAt = row.OccurredAt,
        status = row.Status,
        statusLabel = StatusWords.Label(row.Status),
        typeCode = row.TypeCode
    };
}
