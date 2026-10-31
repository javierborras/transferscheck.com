namespace TransfersCheck.Infrastructure;

/// <summary>Words and colors for one commerce on the owner client list. Red wins, then amber, then green.</summary>
public static class OwnerClientStatus
{
    public const int SoonDays = 7;

    public static string Access(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is null) return "sin";
        if (until <= now) return "vencido";
        if (until <= now.AddDays(SoonDays)) return "vence";
        return "vigente";
    }

    public static bool TrialActive(DateTimeOffset? trialEndsAt, DateTimeOffset now)
        => trialEndsAt is { } trial && trial > now;

    public static bool TrialEnding(DateTimeOffset? trialEndsAt, DateTimeOffset now)
        => trialEndsAt is { } trial && trial > now && trial <= now.AddDays(SoonDays);

    public static string MercadoPago(bool linked, bool reconnect)
        => linked ? "vinculado" : reconnect ? "reconectar" : "sin_vincular";

    public static (string Tone, string Label) Tone(string mercadoPago, string access, bool pendingPayment, bool trialEnding)
    {
        if (mercadoPago == "reconectar") return ("rojo", "Hay que volver a vincular");
        if (access == "vencido") return ("rojo", "Acceso vencido");
        if (pendingPayment) return ("rojo", "Orden pendiente de pago");
        if (mercadoPago == "sin_vincular") return ("ambar", "Sin vincular");
        if (trialEnding) return ("ambar", "Prueba por vencer");
        if (access == "vence") return ("ambar", "Acceso por vencer");
        if (access == "sin") return ("ambar", "Sin acceso");
        return ("verde", "Vinculado");
    }
}
