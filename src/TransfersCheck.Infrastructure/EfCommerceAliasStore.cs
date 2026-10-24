using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfCommerceAliasStore(TransfersCheckDb db) : ICommerceAliasStore
{
    public async Task<string?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == tenantId, cancellationToken);
        return string.IsNullOrEmpty(merchant?.Alias) ? null : merchant.Alias;
    }

    public async Task<ConfirmAliasResult> ConfirmAsync(Guid tenantId, string rawAlias, CancellationToken cancellationToken)
    {
        if (!CommerceAlias.TryNormalize(rawAlias, out var alias))
            return ConfirmAliasResult.Invalid;

        var merchant = await db.Merchants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == tenantId, cancellationToken);
        if (merchant is null)
            return ConfirmAliasResult.Invalid;
        if (!string.IsNullOrEmpty(merchant.Alias))
            return ConfirmAliasResult.AlreadySet;

        var taken = await db.Merchants.IgnoreQueryFilters()
            .AnyAsync(row => row.Alias == alias && row.Id != tenantId, cancellationToken);
        if (taken)
            return ConfirmAliasResult.Taken;

        merchant.Alias = alias;
        await db.SaveChangesAsync(cancellationToken);
        return ConfirmAliasResult.Confirmed;
    }
}
