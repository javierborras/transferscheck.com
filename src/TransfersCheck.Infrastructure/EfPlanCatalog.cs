using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfPlanCatalog(TransfersCheckDb db) : IPlanCatalog
{
    public async Task<IReadOnlyList<Plan>> ListActiveAsync(CancellationToken cancellationToken)
        => await db.Plans.AsNoTracking().Where(plan => plan.Active).OrderBy(plan => plan.Amount).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Plan>> ListAllAsync(CancellationToken cancellationToken)
        => await db.Plans.AsNoTracking().OrderBy(plan => plan.Amount).ThenBy(plan => plan.Code).ToListAsync(cancellationToken);

    public Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken)
        => db.Plans.AsNoTracking().FirstOrDefaultAsync(plan => plan.Code == code, cancellationToken);

    public async Task UpsertAsync(Plan plan, CancellationToken cancellationToken)
    {
        var existing = await db.Plans.FirstOrDefaultAsync(row => row.Code == plan.Code, cancellationToken);
        if (existing is null)
        {
            db.Plans.Add(plan);
        }
        else
        {
            existing.DisplayName = plan.DisplayName;
            existing.Amount = plan.Amount;
            existing.Currency = plan.Currency;
            existing.DurationDays = plan.DurationDays;
            existing.DurationHours = plan.DurationHours;
            existing.FaceCopy = plan.FaceCopy;
            existing.EnableCopilotHelp = plan.EnableCopilotHelp;
            existing.Active = plan.Active;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
