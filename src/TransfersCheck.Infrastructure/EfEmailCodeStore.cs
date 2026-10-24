using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class EfEmailCodeStore(TransfersCheckDb db) : IEmailCodeStore
{
    // Plain codes for Development/e2e peek only; production never reads this map from HTTP outside IsDevelopment.
    private static readonly ConcurrentDictionary<string, string> PlainByEmail = new(StringComparer.OrdinalIgnoreCase);

    public async Task IssueAsync(Guid accountId, string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var existing = await db.EmailSignInChallenges.IgnoreQueryFilters()
            .Where(row => row.AccountId == accountId)
            .ToListAsync(cancellationToken);
        db.EmailSignInChallenges.RemoveRange(existing);
        db.EmailSignInChallenges.Add(new EmailSignInChallenge
        {
            AccountId = accountId,
            Email = normalized,
            CodeHash = EmailSignInCode.Hash(code),
            ExpiresAt = expiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
        PlainByEmail[normalized] = code;
    }

    public async Task<bool> TryConsumeAsync(Guid accountId, string code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!EmailSignInCode.LooksLikeCode(code))
            return false;
        var row = await db.EmailSignInChallenges.IgnoreQueryFilters()
            .Where(item => item.AccountId == accountId && item.ConsumedAt == null)
            .OrderByDescending(item => item.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || row.ExpiresAt <= now || !EmailSignInCode.Matches(code, row.CodeHash))
            return false;
        row.ConsumedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        PlainByEmail.TryRemove(row.Email, out _);
        return true;
    }

    public Task<string?> PeekPlainCodeAsync(string email, CancellationToken cancellationToken)
    {
        PlainByEmail.TryGetValue(email.Trim().ToLowerInvariant(), out var code);
        return Task.FromResult(code);
    }
}
