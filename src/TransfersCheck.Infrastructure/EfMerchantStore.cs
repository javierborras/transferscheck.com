using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfMerchantStore(TransfersCheckDb db, ITenantContext tenant) : IMerchantStore
{
    public async Task AddMerchantAsync(Merchant merchant, CancellationToken cancellationToken)
    {
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<Merchant?> FindMerchantAsync(Guid tenantId, CancellationToken cancellationToken)
        => db.Merchants.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == tenantId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => db.SaveChangesAsync(cancellationToken);

    public Task<LoginAccount?> FindAccountByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return db.LoginAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Email == normalized, cancellationToken);
    }

    public Task<LoginAccount?> FindAccountByAuth0SubjectAsync(string subject, CancellationToken cancellationToken)
    {
        var normalized = subject.Trim();
        return db.LoginAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Auth0Subject == normalized, cancellationToken);
    }

    public Task<LoginAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken)
        => db.LoginAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

    public async Task<IReadOnlyList<LoginAccount>> ListAccountsAsync(CancellationToken cancellationToken)
        => await db.LoginAccounts.OrderBy(a => a.Email).ToListAsync(cancellationToken);

    public Task<int> CountAccountsAsync(CancellationToken cancellationToken)
        => db.LoginAccounts.CountAsync(cancellationToken);

    public async Task AddAccountAsync(LoginAccount account, CancellationToken cancellationToken)
    {
        account.Email = account.Email.Trim().ToLowerInvariant();
        db.LoginAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddSessionAsync(LoginSession session, CancellationToken cancellationToken)
    {
        db.LoginSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        => db.LoginSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public async Task AddOAuthAttemptAsync(OAuthAttempt attempt, CancellationToken cancellationToken)
    {
        db.OAuthAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<OAuthAttempt?> FindOAuthAttemptAsync(string state, CancellationToken cancellationToken)
        => db.OAuthAttempts.FirstOrDefaultAsync(a => a.State == state, cancellationToken);

    public async Task DeleteOAuthAttemptAsync(OAuthAttempt attempt, CancellationToken cancellationToken)
    {
        db.OAuthAttempts.Remove(attempt);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<MerchantConnection?> FindConnectionAsync(CancellationToken cancellationToken)
        => db.Connections.FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExternalAccountLinkedElsewhereAsync(string externalAccountId, Guid tenantId, CancellationToken cancellationToken)
        => db.Connections.IgnoreQueryFilters().AnyAsync(
            c => c.ExternalAccountId == externalAccountId && c.TenantId != tenantId,
            cancellationToken);

    public async Task AddConnectionAsync(MerchantConnection connection, CancellationToken cancellationToken)
    {
        db.Connections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transfer>> ListTransfersAsync(int take, CancellationToken cancellationToken)
        => await db.Transfers.OrderByDescending(t => t.OccurredAt).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TransferChange>> UpsertTransfersAsync(
        string source,
        IReadOnlyList<IncomingTransfer> incoming,
        CancellationToken cancellationToken)
    {
        var ids = incoming.Select(i => i.ExternalId).ToArray();
        var existing = await db.Transfers
            .Where(t => t.Source == source && ids.Contains(t.ExternalId))
            .ToListAsync(cancellationToken);
        var changes = new List<TransferChange>();
        foreach (var item in incoming)
        {
            var row = existing.FirstOrDefault(t => t.ExternalId == item.ExternalId);
            if (row is null)
            {
                row = new Transfer
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.TenantId,
                    Source = source,
                    ExternalId = item.ExternalId,
                    PayerName = item.PayerName,
                    Amount = item.Amount,
                    Currency = item.Currency,
                    OccurredAt = item.OccurredAt,
                    Status = item.Status,
                    TypeCode = item.TypeCode
                };
                db.Transfers.Add(row);
                changes.Add(new TransferChange(row.Id, row.ExternalId, row.PayerName, row.Amount, row.OccurredAt, row.Status, StatusWords.MakesSound(item.Status)));
            }
            else
            {
                var becameArrived = !StatusWords.MakesSound(row.Status) && StatusWords.MakesSound(item.Status);
                row.PayerName = item.PayerName;
                row.Amount = item.Amount;
                row.Currency = item.Currency;
                row.OccurredAt = item.OccurredAt;
                row.Status = item.Status;
                row.TypeCode = item.TypeCode;
                if (becameArrived)
                    changes.Add(new TransferChange(row.Id, row.ExternalId, row.PayerName, row.Amount, row.OccurredAt, row.Status, true));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return changes;
    }

    public async Task AddPushDeviceAsync(PushDevice device, CancellationToken cancellationToken)
    {
        var existing = await db.PushDevices.FirstOrDefaultAsync(d => d.AccountId == device.AccountId, cancellationToken);
        if (existing is null)
            db.PushDevices.Add(device);
        else
            existing.Token = device.Token;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasPushDeviceAsync(CancellationToken cancellationToken)
        => db.PushDevices.AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<PushDevice>> ListApprovedPushDevicesAsync(CancellationToken cancellationToken)
    {
        // Wave 15: only accounts with Habilitar Notificaciones (PushApproved).
        var enabled = db.LoginAccounts.Where(a => a.PushApproved && !a.Revoked).Select(a => a.Id);
        return await db.PushDevices.Where(d => enabled.Contains(d.AccountId)).ToListAsync(cancellationToken);
    }

    public async Task DeletePushDevicesForAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var rows = await db.PushDevices.Where(d => d.AccountId == accountId).ToListAsync(cancellationToken);
        db.PushDevices.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> PaymentAlreadyAppliedAsync(string paymentId, CancellationToken cancellationToken)
        => db.ProcessedPayments.IgnoreQueryFilters().AnyAsync(p => p.PaymentId == paymentId, cancellationToken);

    public async Task AddProcessedPaymentAsync(ProcessedPayment payment, CancellationToken cancellationToken)
    {
        db.ProcessedPayments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListConnectedTenantIdsAsync(CancellationToken cancellationToken)
        => await db.Connections.IgnoreQueryFilters().Select(c => c.TenantId).Distinct().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListStoredTypeCodesAsync(CancellationToken cancellationToken)
        => await db.TransferTypes.OrderBy(t => t.Code).Select(t => t.Code).ToListAsync(cancellationToken);
}
