using System.Security.Cryptography;
using System.Text;

namespace TransfersCheck.Core;

public static class Pkce
{
    public static string Verifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    public static string Challenge(string verifier)
        => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    public static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
