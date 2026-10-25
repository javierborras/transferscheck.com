using System.Security.Cryptography;
using System.Text;

namespace TransfersCheck.Core;

/// <summary>
/// Signs and verifies the per-order <c>notification_url</c> sent server-to-server to Mercado Pago when creating a Checkout Pro preference (ADR-022).
/// </summary>
public static class CheckoutNotificationSignature
{
    public static string SignReference(string reference, string signingKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        var payload = Encoding.UTF8.GetBytes($"tc-webhook-ref:{reference}");
        return Convert.ToHexString(HMACSHA256.HashData(keyBytes, payload)).ToLowerInvariant();
    }

    public static string BuildNotificationUrl(string baseUrl, string reference, string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            return baseUrl;
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var sig = SignReference(reference, signingKey);
        return $"{baseUrl}{separator}ref={Uri.EscapeDataString(reference)}&sig={sig}";
    }

    public static bool VerifyReference(string? reference, string? signature, string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            return false;
        var expected = SignReference(reference, signingKey);
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant())));
    }
}
