using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class TenantPlanCode(IMerchantStore store, ITenantContext tenant) : ITenantPlanCode
{
    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        var merchant = await store.FindMerchantAsync(tenant.TenantId, cancellationToken);
        return merchant?.PlanCode;
    }
}
