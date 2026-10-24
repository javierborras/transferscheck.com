using TransfersCheck.Core;

namespace TransfersCheck.Api.Jobs;

public sealed class ReferralBonusJob(IReferralStore referrals, IPlanCatalog plans, TimeProvider clock)
{
    public async Task Execute()
    {
        var packMes = await plans.FindByCodeAsync(PlanCodes.PackMes, CancellationToken.None);
        var days = packMes?.DurationDays ?? 30;
        await referrals.ProcessPendingAsync(clock.GetUtcNow(), days, CancellationToken.None);
    }
}
