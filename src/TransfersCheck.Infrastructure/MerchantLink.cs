using System.Net;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public static class MerchantLink
{
    public static bool IsUnauthorized(HttpRequestException exception)
        => exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public static void StoreTokens(MerchantConnection connection, TokenSet refreshed, string previousRefresh, ITokenProtector protector)
    {
        var refresh = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? previousRefresh : refreshed.RefreshToken;
        connection.AccessTokenProtected = protector.Protect(refreshed.AccessToken);
        connection.RefreshTokenProtected = protector.Protect(refresh);
        connection.AccessTokenExpiresAt = refreshed.ExpiresAt;
        connection.ReconnectRequired = false;
    }

    public static void RequireReconnect(MerchantConnection connection, Merchant merchant)
    {
        connection.ReconnectRequired = true;
        merchant.RefreshNotBefore = null;
        merchant.RateLimitedUntil = null;
    }
}
