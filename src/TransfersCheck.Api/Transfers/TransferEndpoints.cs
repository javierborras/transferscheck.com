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
        group.MapPost("/transfers/refresh", Refresh);
    }

    private static async Task<IResult> List(
        Guid? first,
        HttpContext http,
        IMerchantStore store,
        IOptions<SyncOptions> sync,
        CancellationToken cancellationToken)
    {
        var rows = (await store.ListTransfersAsync(sync.Value.PageSize, cancellationToken)).ToList();
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
            items = rows.Select(row => new
            {
                id = row.Id,
                payerName = row.PayerName,
                amount = row.Amount,
                occurredAt = row.OccurredAt,
                status = row.Status,
                statusLabel = StatusWords.Label(row.Status),
                typeCode = row.TypeCode
            }),
            pollIntervalSeconds = (int)sync.Value.PollInterval.TotalSeconds,
            buttonCooldownSeconds = (int)sync.Value.ButtonCooldown.TotalSeconds
        });
    }

    private static async Task<IResult> Refresh(
        HttpContext http,
        TransferSync sync,
        IMerchantStore store,
        IOptions<SyncOptions> options,
        CancellationToken cancellationToken)
    {
        var account = (LoginAccount)http.Items["account"]!;
        var status = await sync.Run(account.TenantId, manual: true, cancellationToken);
        if (status is SyncStatus.Waiting)
            return Results.Json(new { error = "waiting" }, statusCode: StatusCodes.Status429TooManyRequests);
        if (status is SyncStatus.NotEntitled)
            return Results.Json(new { error = "not_entitled" }, statusCode: StatusCodes.Status409Conflict);

        var rows = await store.ListTransfersAsync(options.Value.PageSize, cancellationToken);
        return Results.Ok(new { count = rows.Count });
    }
}
