namespace TransfersCheck.Core;

public static class PlanCodes
{
    public const string PackEvento = "pack_evento";
    public const string PackMes = "pack_mes";
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
        return false;
    }
}

public interface ITenantPlanCode
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}
