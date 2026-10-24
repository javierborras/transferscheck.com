using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfFiveDayNoticeStore(TransfersCheckDb db) : IFiveDayNoticeStore
{
    public async Task<IReadOnlyList<NoticeCandidate>> ListCandidatesAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from merchant in db.Merchants.IgnoreQueryFilters()
            join account in db.LoginAccounts.IgnoreQueryFilters()
                on merchant.Id equals account.TenantId
            where account.IsOwner && !account.Revoked && merchant.AccessUntil != null
            select new NoticeCandidate(
                merchant.Id,
                account.Email,
                merchant.PlanCode,
                merchant.AccessUntil!.Value,
                merchant.FiveDayNoticeSentAt)
        ).ToListAsync(cancellationToken);
        return rows;
    }

    public async Task MarkSentAsync(Guid tenantId, DateTimeOffset sentAt, CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == tenantId, cancellationToken);
        if (merchant is null)
            return;
        merchant.FiveDayNoticeSentAt = sentAt;
        await db.SaveChangesAsync(cancellationToken);
    }
}
