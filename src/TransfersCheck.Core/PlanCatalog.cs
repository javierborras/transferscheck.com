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
    Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken);
}

/// <summary>
/// Starts the Pack Mes trial from the Mercado Pago link instant (21 days, same clock).
/// </summary>
public static class PackMesTrial
{
    public const int Days = 21;

    public static bool Applies(string? planCode)
        => string.Equals(planCode, PlanCodes.PackMes, StringComparison.OrdinalIgnoreCase);

    public static void ApplyLink(Merchant merchant, DateTimeOffset linkInstant, IEntitlement entitlement)
    {
        merchant.ConnectedAt ??= linkInstant;
        if (!Applies(merchant.PlanCode) || merchant.TrialEndsAt is not null)
            return;

        var end = entitlement.StartTrial(linkInstant, Days);
        merchant.TrialEndsAt = end;
        merchant.AccessUntil = end;
    }
}
