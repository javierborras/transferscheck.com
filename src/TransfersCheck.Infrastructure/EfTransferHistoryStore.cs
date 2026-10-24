using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfTransferHistoryStore(TransfersCheckDb db) : ITransferHistoryStore
{
    public async Task<IReadOnlyList<HistoryRow>> QueryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var query = db.Transfers.AsNoTracking().AsQueryable();
        if (from is { } start)
            query = query.Where(row => row.OccurredAt >= start);
        if (to is { } end)
            query = query.Where(row => row.OccurredAt <= end);
        var rows = await query.OrderByDescending(row => row.OccurredAt).ToListAsync(cancellationToken);
        return rows.Select(row => new HistoryRow(row.Id, row.OccurredAt, row.PayerName, row.Amount, row.Status)).ToList();
    }
}
