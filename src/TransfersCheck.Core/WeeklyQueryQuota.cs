namespace TransfersCheck.Core;

public enum QuotaLevel
{
    Ok,
    Warn,
    Wait
}

/// <summary>
/// Weekly consulta policy: a consulta is an on-demand transfer refresh/query.
/// Warn at 450 used (gap 50 before wall 500), wait at 500, seven days from the Mercado Pago link.
/// </summary>
public static class WeeklyQueryQuota
{
    public static QuotaLevel Evaluate(int usedCount, int weeklyQueryCap, int weeklyQueryWarningGap)
    {
        if (usedCount >= weeklyQueryCap)
            return QuotaLevel.Wait;
        if (usedCount >= weeklyQueryCap - weeklyQueryWarningGap)
            return QuotaLevel.Warn;
        return QuotaLevel.Ok;
    }

    public static (DateTimeOffset Start, DateTimeOffset End) Period(DateTimeOffset linkInstant, DateTimeOffset now)
    {
        if (now < linkInstant)
            return (linkInstant, linkInstant.AddDays(7));

        var elapsed = now - linkInstant;
        var periods = (long)(elapsed.TotalDays / 7);
        var start = linkInstant.AddDays(periods * 7);
        return (start, start.AddDays(7));
    }
}

public interface IConsultaCounter
{
    Task<int> CountAsync(Guid tenantId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken);
    Task<int> IncrementOnDemandAsync(Guid tenantId, DateTimeOffset at, DateTimeOffset periodStart, CancellationToken cancellationToken);
}
