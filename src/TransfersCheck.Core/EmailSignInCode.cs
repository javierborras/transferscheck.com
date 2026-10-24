using System.Security.Cryptography;

namespace TransfersCheck.Core;

public sealed class EmailSignInChallenge
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public interface IEmailCodeStore
{
    Task IssueAsync(Guid accountId, string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<bool> TryConsumeAsync(Guid accountId, string code, DateTimeOffset now, CancellationToken cancellationToken);
    Task<string?> PeekPlainCodeAsync(string email, CancellationToken cancellationToken);
}

/// <summary>Issues and checks the 6-digit email sign-in code (1-hour lifetime from options).</summary>
public static class EmailSignInCode
{
    public const int Digits = 6;

    public static string Generate()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString($"D{Digits}");
    }

    public static bool LooksLikeCode(string? code)
        => code is { Length: Digits } && code.All(char.IsDigit);

    public static string Hash(string code)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code.Trim()));
        return Convert.ToHexString(bytes);
    }

    public static bool Matches(string code, string codeHash)
        => string.Equals(Hash(code), codeHash, StringComparison.OrdinalIgnoreCase);
}
