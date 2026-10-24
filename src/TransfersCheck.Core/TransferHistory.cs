namespace TransfersCheck.Core;

public sealed record HistoryRow(Guid Id, DateTimeOffset OccurredAt, string PayerName, decimal Amount, string Status);

public sealed record HistoryTotals(decimal ByDay, decimal ByMonth, decimal ByRange);

public interface ITransferHistoryStore
{
    Task<IReadOnlyList<HistoryRow>> QueryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

public static class TransferHistory
{
    public static string DisplayPayer(string payerName)
        => string.IsNullOrWhiteSpace(payerName) ? "Desconocido" : payerName;

    public static HistoryTotals Totals(IReadOnlyList<HistoryRow> rows, DateTimeOffset now)
    {
        var dayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        decimal Sum(Func<HistoryRow, bool> pred) => rows.Where(pred).Sum(row => row.Amount);
        return new HistoryTotals(
            Sum(row => row.OccurredAt >= dayStart),
            Sum(row => row.OccurredAt >= monthStart),
            rows.Sum(row => row.Amount));
    }

    public static string ToCsv(IReadOnlyList<HistoryRow> rows)
    {
        var lines = new List<string> { "Fecha,Pagador,Monto,Estado" };
        foreach (var row in rows)
        {
            var fecha = row.OccurredAt.ToString("dd/MM/yyyy HH:mm");
            var payer = DisplayPayer(row.PayerName).Replace("\"", "\"\"", StringComparison.Ordinal);
            lines.Add($"{fecha},\"{payer}\",{row.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{StatusWords.Label(row.Status)}");
        }
        return string.Join("\n", lines) + "\n";
    }
}
