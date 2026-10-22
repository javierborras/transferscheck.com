using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TransfersCheck.Core;

namespace TransfersCheck.Api.Auth;

public static class SessionIssuer
{
    public static string Issue(LoginAccount account, Guid sessionId, DateTimeOffset expires, string signingKey)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
                new Claim("tid", account.TenantId.ToString()),
                new Claim("sid", sessionId.ToString()),
                new Claim("owner", account.IsOwner ? "1" : "0")
            ],
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static void AppendCookie(HttpResponse response, LoginSessionOptions options, string token)
    {
        response.Cookies.Append(options.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            MaxAge = options.Lifetime,
            Path = "/",
            IsEssential = true
        });
    }
}
