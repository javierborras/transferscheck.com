using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfPlanCatalog(TransfersCheckDb db) : IPlanCatalog
{
    public async Task<IReadOnlyList<Plan>> ListActiveAsync(CancellationToken cancellationToken)
        => await db.Plans.AsNoTracking().Where(plan => plan.Active).OrderBy(plan => plan.Amount).ToListAsync(cancellationToken);

    public Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken)
        => db.Plans.AsNoTracking().FirstOrDefaultAsync(plan => plan.Code == code, cancellationToken);
}
