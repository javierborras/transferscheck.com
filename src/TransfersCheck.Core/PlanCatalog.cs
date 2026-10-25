namespace TransfersCheck.Core;

public sealed class Plan
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public int? DurationDays { get; set; }
    public int? DurationHours { get; set; }
    public string FaceCopy { get; set; } = "";
    public bool EnableCopilotHelp { get; set; }
    public bool Active { get; set; } = true;
}

public interface IPlanCatalog
{
    Task<IReadOnlyList<Plan>> ListActiveAsync(CancellationToken cancellationToken);
    /// <summary>Owner microsite: every plan row, including inactive (story 12.2).</summary>
    Task<IReadOnlyList<Plan>> ListAllAsync(CancellationToken cancellationToken);
    Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken);
    Task UpsertAsync(Plan plan, CancellationToken cancellationToken);
}

/// <summary>
/// Starts only the Pack Mes 21-day trial at linking; Individual requires a paid explicit start.
/// </summary>
public static class PackMesTrial
{
    public const int Days = 21;

    public static bool Applies(string? planCode)
        => string.Equals(planCode, PlanCodes.PackMes, StringComparison.OrdinalIgnoreCase);

    public static void ApplyLink(Merchant merchant, DateTimeOffset linkInstant, IEntitlement entitlement)
    {
        merchant.ConnectedAt ??= linkInstant;
        if (merchant.AccessUntil is not null)
            return;

        if (Applies(merchant.PlanCode))
        {
            if (merchant.TrialEndsAt is not null)
                return;
            var end = entitlement.StartTrial(linkInstant, Days);
            merchant.TrialEndsAt = end;
            merchant.AccessUntil = end;
            return;
        }

        // ADR-021: Individual starts only after a paid order is explicitly activated.
    }
}

public static class AccessBlock
{
    public static bool IsBlocked(DateTimeOffset? accessUntil, DateTimeOffset now)
        => accessUntil is { } until && until <= now;
}
