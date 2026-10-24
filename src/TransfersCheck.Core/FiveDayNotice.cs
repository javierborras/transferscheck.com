namespace TransfersCheck.Core;

public sealed record NoticeCandidate(
    Guid TenantId,
    string OwnerEmail,
    string? PlanCode,
    DateTimeOffset AccessUntil,
    DateTimeOffset? NoticeSentAt);

public interface IMailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken);
}

public interface IFiveDayNoticeStore
{
    Task<IReadOnlyList<NoticeCandidate>> ListCandidatesAsync(CancellationToken cancellationToken);
    Task MarkSentAsync(Guid tenantId, DateTimeOffset sentAt, CancellationToken cancellationToken);
}

/// <summary>
/// Decides whether the five-day access warning applies (Pack Evento is excluded).
/// </summary>
public static class FiveDayNotice
{
    public const int DaysBefore = 5;

    public static bool ShouldWarn(string? planCode, DateTimeOffset? accessUntil, DateTimeOffset now)
    {
        // Unset plan code follows Pack Evento (login caps); no five-day warning.
        if (string.IsNullOrWhiteSpace(planCode)
            || string.Equals(planCode, PlanCodes.PackEvento, StringComparison.OrdinalIgnoreCase))
            return false;
        if (accessUntil is not { } until || until <= now)
            return false;
        return until <= now.AddDays(DaysBefore);
    }

    public static bool ShouldSend(NoticeCandidate candidate, DateTimeOffset now)
        => ShouldWarn(candidate.PlanCode, candidate.AccessUntil, now) && candidate.NoticeSentAt is null;
}
