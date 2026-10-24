using TransfersCheck.Api.Auth;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Transfers;

public static class HistoryEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter<LiveSessionFilter>();
        group.MapGet("/history", Query);
        group.MapGet("/history/csv", Csv);
    }

    private static async Task<IResult> Query(
        DateTimeOffset? from,
        DateTimeOffset? to,
        HttpContext http,
        ITransferHistoryStore history,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var rows = await history.QueryAsync(from, to, cancellationToken);
        var now = clock.GetUtcNow();
        var totals = TransferHistory.Totals(rows, now);
        return Results.Ok(new
        {
            title = "Historial de Transacciones",
            columns = new[] { "Fecha", "Pagador", "Monto", "Estado" },
            items = rows.Select(row => new
            {
                id = row.Id,
                occurredAt = row.OccurredAt,
                payerName = TransferHistory.DisplayPayer(row.PayerName),
                amount = row.Amount,
                status = row.Status,
                statusLabel = StatusWords.Label(row.Status)
            }),
            totals = new { byDay = totals.ByDay, byMonth = totals.ByMonth, byRange = totals.ByRange }
        });
    }

    private static async Task<IResult> Csv(
        DateTimeOffset? from,
        DateTimeOffset? to,
        HttpContext http,
        ITransferHistoryStore history,
        CancellationToken cancellationToken)
    {
        if (!Owner(http))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var rows = await history.QueryAsync(from, to, cancellationToken);
        return Results.Text(TransferHistory.ToCsv(rows), "text/csv");
    }

    private static bool Owner(HttpContext http)
        => http.User.FindFirst("owner")?.Value == "1";
}
