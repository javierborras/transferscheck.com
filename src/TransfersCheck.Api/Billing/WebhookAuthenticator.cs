using System.Security.Cryptography;
using System.Text;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Billing;

/// <summary>
/// Authenticates a Checkout Pro notification (ADR-022):
/// 1. Official <c>x-signature: ts=…,v1=…</c> HMAC-SHA256 when <see cref="CheckoutOptions.NotificationSigningKey"/> is configured.
/// 2. Server-signed <c>notification_url</c> (<c>?ref=…&amp;sig=…</c>) generated per preference with <see cref="LoginSessionOptions.SigningKey"/>,
///    paired with authenticated reconciliation against Mercado Pago <c>/v1/payments/{id}</c> and exact reference match.
/// 3. Shared header <c>X-Webhook-Secret</c> only in Development/Test when <see cref="CheckoutOptions.WebhookSecret"/> is set.
/// </summary>
public static class WebhookAuthenticator
{
    public static bool IsAuthentic(
        CheckoutOptions options,
        string? sharedHeader,
        string? signature,
        string? requestId,
        string? dataId,
        DateTimeOffset now,
        string? sessionSigningKey = null,
        string? signedReference = null,
        string? referenceSignature = null)
    {
        if (!string.IsNullOrWhiteSpace(options.NotificationSigningKey))
            return VerifySignature(options.NotificationSigningKey, signature, requestId, dataId, now, options.NotificationTolerance);
        if (!string.IsNullOrWhiteSpace(signedReference) && !string.IsNullOrWhiteSpace(referenceSignature) && !string.IsNullOrWhiteSpace(sessionSigningKey))
            return CheckoutNotificationSignature.VerifyReference(signedReference, referenceSignature, sessionSigningKey);
        if (options.WebhookSecret.Length == 0) return false;
        return FixedEquals(options.WebhookSecret, sharedHeader ?? "");
    }

    public static bool VerifySignature(string key, string? signature, string? requestId, string? dataId, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(dataId)) return false;
        string? ts = null, v1 = null;
        foreach (var part in signature.Split(',')) {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            switch (pair[0].Trim()) { case "ts": ts = pair[1].Trim(); break; case "v1": v1 = pair[1].Trim(); break; }
        }
        if (ts is null || v1 is null || !long.TryParse(ts, out var stamp)) return false;
        var signedAt = stamp > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(stamp) : DateTimeOffset.FromUnixTimeSeconds(stamp);
        if ((now - signedAt).Duration() > tolerance) return false;
        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{ts};";
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return FixedEquals(expected, v1.ToLowerInvariant());
    }

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(left)), SHA256.HashData(Encoding.UTF8.GetBytes(right)));
}
