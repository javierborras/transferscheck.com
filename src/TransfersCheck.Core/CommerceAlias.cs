using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TransfersCheck.Core;

public sealed class AlertInvite
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public string Token { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public interface ICommerceAliasStore
{
    Task<string?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<ConfirmAliasResult> ConfirmAsync(Guid tenantId, string rawAlias, CancellationToken cancellationToken);
}

public enum ConfirmAliasResult
{
    Confirmed,
    AlreadySet,
    Taken,
    Invalid
}

public interface IAlertInviteStore
{
    Task AddAsync(AlertInvite invite, CancellationToken cancellationToken);
    Task<AlertInvite?> FindByTokenAsync(string token, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>One-time commerce alias shown as #alias beside the logo.</summary>
public static class CommerceAlias
{
    private static readonly Regex Pattern = new("^[a-z0-9][a-z0-9_-]{1,31}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryNormalize(string? raw, out string alias)
    {
        alias = (raw ?? "").Trim().TrimStart('#').ToLowerInvariant();
        return Pattern.IsMatch(alias);
    }

    public static string Display(string alias) => $"#{alias}";
}

/// <summary>Punto de Alerta identity: usuario#alias, not a URL hash.</summary>
public static class AlertPointIdentity
{
    public static string EmailKey(string userName, string alias)
        => $"{NormalizeUser(userName)}#{alias.Trim().ToLowerInvariant()}@punto.local";

    public static string Display(string userName, string alias)
        => $"{NormalizeUser(userName)}#{alias.Trim().ToLowerInvariant()}";

    public static bool TryNormalizeUser(string? raw, out string userName)
    {
        userName = NormalizeUser(raw ?? "");
        return userName.Length is >= 2 and <= 32 && Regex.IsMatch(userName, "^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);
    }

    public static string NewInviteToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string InvitePath(string token) => $"/invitar/{token}";

    public static string WaMeUrl(string inviteAbsoluteUrl)
    {
        var text = $"TransfersCheck — abrí este enlace para crear tu contraseña: {inviteAbsoluteUrl}";
        return "https://wa.me/?text=" + Uri.EscapeDataString(text);
    }

    private static string NormalizeUser(string raw) => raw.Trim().ToLowerInvariant();
}
