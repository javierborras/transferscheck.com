namespace TransfersCheck.Core;

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public interface ITenantContext
{
    Guid TenantId { get; set; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
}

public sealed class Merchant : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId
    {
        get => Id;
        set => Id = value;
    }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AccessUntil { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    public string ChecklistStep { get; set; } = "account";
    public DateTimeOffset? RateLimitedUntil { get; set; }
    public DateTimeOffset? RefreshNotBefore { get; set; }
    /// <summary>Plan code for login caps and the plan table.</summary>
    public string? PlanCode { get; set; }
    public DateTimeOffset? FiveDayNoticeSentAt { get; set; }
    /// <summary>Confirmed commerce alias without #. Null until the admin confirms once.</summary>
    public string? Alias { get; set; }
}

public sealed class LoginAccount : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
    /// <summary>Local user name for a Punto de Alerta (shown as usuario#alias). Empty for the admin.</summary>
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsOwner { get; set; }
    public bool Revoked { get; set; }
    public bool PushApproved { get; set; }
    /// <summary>False until the invite link sets a password.</summary>
    public bool PasswordSet { get; set; } = true;
    public Guid? LiveSessionId { get; set; }
}

public sealed class LoginSession : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReplacedAt { get; set; }
}

public sealed class OAuthAttempt : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string State { get; set; } = "";
    public string CodeVerifierProtected { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class MerchantConnection : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Source { get; set; } = "mercadopago";
    public string ExternalAccountId { get; set; } = "";
    public string AccessTokenProtected { get; set; } = "";
    public string RefreshTokenProtected { get; set; } = "";
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
}

public sealed class TransferType
{
    public string Code { get; set; } = "";
}

public sealed class Transfer : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Source { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string PayerName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public DateTimeOffset OccurredAt { get; set; }
    public string Status { get; set; } = "";
    public string TypeCode { get; set; } = "";
}

public sealed class PushDevice : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public string Token { get; set; } = "";
}

public sealed class ProcessedPayment : ITenantOwned
{
    public string PaymentId { get; set; } = "";
    public Guid TenantId { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}

public static class SqlCompat
{
    public const int Level = 150;
}

public static class StatusWords
{
    public static string Label(string status) => status switch
    {
        "approved" => "Llegó",
        "pending" => "Pendiente",
        "rejected" => "Rechazado",
        _ => status
    };

    public static bool MakesSound(string status) => status == "approved";
}

public static class ChecklistRules
{
    public static string Step(bool accountExists, bool connectionExists, bool pushTokenExists)
    {
        if (!accountExists)
            return "account";
        if (!connectionExists)
            return "connect";
        if (!pushTokenExists)
            return "alerts";
        return "ready";
    }
}
