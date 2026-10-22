using Microsoft.AspNetCore.DataProtection;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class DataProtectionTokenProtector(IDataProtectionProvider provider) : ITokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("TransfersCheck.Tokens.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string payload) => _protector.Unprotect(payload);
}
