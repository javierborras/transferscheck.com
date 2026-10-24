using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class ConsultaEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class EfConsultaCounter(TransfersCheckDb db) : IConsultaCounter
{
    public Task<int> CountAsync(Guid tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken)
        => db.ConsultaEvents.IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId && row.OccurredAt >= periodStart && row.OccurredAt < periodEnd, cancellationToken);

    public async Task<int> IncrementOnDemandAsync(Guid tenantId, DateTimeOffset at, DateTimeOffset periodStart, CancellationToken cancellationToken)
    {
        db.ConsultaEvents.Add(new ConsultaEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OccurredAt = at
        });
        await db.SaveChangesAsync(cancellationToken);
        var periodEnd = periodStart.AddDays(7);
        return await CountAsync(tenantId, periodStart, periodEnd, cancellationToken);
    }
}
