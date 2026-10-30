using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed record IncidentLatchRow(
    string Fingerprint,
    int Count,
    string? ReleaseTag,
    string? SentryId,
    string? SentryUrl,
    int? GitHubIssueNumber,
    string? IssueUrl,
    string? PullRequestUrl);

public sealed record CommerceMessageRow(
    Guid Id,
    string? Alias,
    string Email,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>
/// Incident rows latch a fingerprint (ADR-026). Commerce messages are a separate list (ADR-032).
/// </summary>
public static class HelpdeskStore
{
    public const int FingerprintMaxLength = 128;
    public const int TextMaxLength = 1000;
    public const int Take = 200;
    public const string GitHubRepository = "javierborras/transferscheck.com";

    public static async Task TouchAsync(
        TransfersCheckDb db,
        string fingerprint,
        string? releaseTag,
        string? sentryId,
        int? gitHubIssueNumber,
        string? pullRequestUrl,
        CancellationToken cancellationToken)
    {
        var key = fingerprint.Trim();
        if (key.Length == 0 || key.Length > FingerprintMaxLength)
            throw new ArgumentException("Fingerprint is required.", nameof(fingerprint));
        var tag = Clean(releaseTag, 64, nameof(releaseTag));
        var sentry = Clean(sentryId, 256, nameof(sentryId));
        var pull = Clean(pullRequestUrl, 512, nameof(pullRequestUrl));

        var row = await db.IncidentLatches.FirstOrDefaultAsync(item => item.Fingerprint == key, cancellationToken);
        if (row is null)
        {
            row = new IncidentLatch { Fingerprint = key, Count = 1 };
            db.IncidentLatches.Add(row);
        }
        else
        {
            row.Count++;
        }

        row.LastSeenAt = DateTimeOffset.UtcNow;
        if (tag is not null)
            row.ReleaseTag = tag;
        if (sentry is not null)
            row.SentryId = sentry;
        if (gitHubIssueNumber is > 0)
            row.GitHubIssueNumber = gitHubIssueNumber;
        if (pull is not null)
            row.PullRequestUrl = pull;
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<IncidentLatchRow>> ListIncidentsAsync(TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var rows = await db.IncidentLatches.AsNoTracking()
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.LastSeenAt)
            .Take(Take)
            .ToListAsync(cancellationToken);
        return rows.Select(item => new IncidentLatchRow(
            item.Fingerprint,
            item.Count,
            Blank(item.ReleaseTag),
            Blank(item.SentryId),
            SentryUrl(item.SentryId),
            item.GitHubIssueNumber,
            IssueUrl(item.GitHubIssueNumber),
            Blank(item.PullRequestUrl))).ToList();
    }

    public static async Task<Guid> CreateMessageAsync(
        TransfersCheckDb db,
        Guid tenantId,
        string text,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var alias = await db.Merchants.AsNoTracking()
            .Where(merchant => merchant.Id == tenantId)
            .Select(merchant => merchant.Alias)
            .FirstOrDefaultAsync(cancellationToken);
        var email = await db.LoginAccounts.AsNoTracking()
            .Where(account => account.TenantId == tenantId && account.IsOwner && !account.Revoked)
            .Select(account => account.Email)
            .FirstOrDefaultAsync(cancellationToken) ?? "";
        var row = new CommerceHelpMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Alias = alias ?? "",
            AdminEmail = email,
            Text = text,
            CreatedAt = now
        };
        db.CommerceHelpMessages.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public static async Task<IReadOnlyList<CommerceMessageRow>> ListMessagesAsync(TransfersCheckDb db, CancellationToken cancellationToken)
    {
        var rows = await db.CommerceHelpMessages.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .Take(Take)
            .Select(item => new CommerceMessageRow(
                item.Id,
                item.Alias,
                item.AdminEmail,
                item.Text,
                item.CreatedAt,
                item.ClosedAt))
            .ToListAsync(cancellationToken);
        return rows.Select(item => item with { Alias = Blank(item.Alias) }).ToList();
    }

    public static async Task<CommerceMessageRow?> CloseMessageAsync(
        TransfersCheckDb db,
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await db.CommerceHelpMessages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
            return null;
        row.ClosedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        return new CommerceMessageRow(row.Id, Blank(row.Alias), row.AdminEmail, row.Text, row.CreatedAt, row.ClosedAt);
    }

    public static string? IssueUrl(int? number)
        => number is > 0 ? $"https://github.com/{GitHubRepository}/issues/{number.Value}" : null;

    public static string? SentryUrl(string? sentryId)
    {
        var value = Blank(sentryId);
        return value is not null && value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    private static string? Clean(string? value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Value is longer than {maxLength}.", name);
        return trimmed;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
