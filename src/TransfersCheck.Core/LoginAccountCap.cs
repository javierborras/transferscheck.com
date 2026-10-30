namespace TransfersCheck.Core;

public static class PlanCodes
{
    public const string PackEvento = "pack_evento";
    public const string PackMes = "pack_mes";
    public const string PackCorporativo = "pack_corporativo";
}

/// <summary>
/// Decides whether Configurar may add another Punto de Alerta for the tenant plan.
/// </summary>
public sealed class LoginAccountCap
{
    public bool AllowsAnotherAlertPoint(string? planCode, int alertPointCount)
    {
        if (string.Equals(planCode, PlanCodes.PackMes, StringComparison.OrdinalIgnoreCase))
            return alertPointCount < 4;
        if (string.Equals(planCode, PlanCodes.PackCorporativo, StringComparison.OrdinalIgnoreCase))
            return alertPointCount < 5;
        return alertPointCount < 1;
    }
}

public interface ITenantPlanCode
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}
