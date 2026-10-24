using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfAlertInviteStore(TransfersCheckDb db) : IAlertInviteStore
{
    public async Task AddAsync(AlertInvite invite, CancellationToken cancellationToken)
    {
        db.AlertInvites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AlertInvite?> FindByTokenAsync(string token, CancellationToken cancellationToken)
        => db.AlertInvites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Token == token, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => db.SaveChangesAsync(cancellationToken);
}