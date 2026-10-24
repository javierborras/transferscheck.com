using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfReferralStore(TransfersCheckDb db, IEntitlement entitlement) : IReferralStore
{
    public async Task<string> GetOrCreateCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.IgnoreQueryFilters().FirstAsync(row => row.Id == tenantId, cancellationToken);
        if (!string.IsNullOrEmpty(merchant.ReferralCode))
            return merchant.ReferralCode;
        string code;
        do
        {
            code = ReferralCode.Generate();
        } while (await db.Merchants.IgnoreQueryFilters().AnyAsync(row => row.ReferralCode == code, cancellationToken));
        merchant.ReferralCode = code;
        await db.SaveChangesAsync(cancellationToken);
        return code;
    }

    public async Task<bool> AttachCodeAsync(Guid inviteeTenantId, string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            return false;
        var invitee = await db.Merchants.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == inviteeTenantId, cancellationToken);
        if (invitee is null || !string.IsNullOrEmpty(invitee.PendingReferralCode))
            return false;
        var referrer = await db.Merchants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.ReferralCode == normalized && row.Id != inviteeTenantId, cancellationToken);
        if (referrer is null)
            return false;
        invitee.PendingReferralCode = normalized;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task QueueGrantFromPackMesPaymentAsync(Guid inviteeTenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var invitee = await db.Merchants.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == inviteeTenantId, cancellationToken);
        if (invitee is null || string.IsNullOrEmpty(invitee.PendingReferralCode) || invitee.ReferralGrantQueued)
            return;
        var referrer = await db.Merchants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.ReferralCode == invitee.PendingReferralCode, cancellationToken);
        if (referrer is null)
            return;
        db.ReferralGrants.Add(new ReferralGrant
        {
            Id = Guid.NewGuid(),
            InviteeTenantId = inviteeTenantId,
            ReferrerTenantId = referrer.Id,
            CreatedAt = now,
            Outcome = "pending"
        });
        invitee.ReferralGrantQueued = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessPendingAsync(DateTimeOffset now, int packMesDays, CancellationToken cancellationToken)
    {
        var pending = await db.ReferralGrants.IgnoreQueryFilters()
            .Where(row => row.Outcome == "pending")
            .OrderBy(row => row.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var grant in pending)
        {
            var referrer = await db.Merchants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(row => row.Id == grant.ReferrerTenantId, cancellationToken);
            if (referrer is null)
            {
                grant.Outcome = "missing";
                grant.ResolvedAt = now;
                continue;
            }

            if (AccessBlock.IsBlocked(referrer.AccessUntil, now))
            {
                grant.Outcome = "blocked";
                grant.ResolvedAt = now;
                continue;
            }

            if (referrer.ReferralBonusGranted)
            {
                grant.Outcome = "already_granted";
                grant.ResolvedAt = now;
                continue;
            }

            referrer.AccessUntil = entitlement.Extend(referrer.AccessUntil, now, packMesDays);
            referrer.ReferralBonusGranted = true;
            grant.Outcome = "granted";
            grant.ResolvedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
