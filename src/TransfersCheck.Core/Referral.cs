using System.Security.Cryptography;

namespace TransfersCheck.Core;

public sealed class ReferralGrant
{
    public Guid Id { get; set; }
    public Guid InviteeTenantId { get; set; }
    public Guid ReferrerTenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string Outcome { get; set; } = "pending";
}

public interface IReferralStore
{
    Task<string> GetOrCreateCodeAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<bool> AttachCodeAsync(Guid inviteeTenantId, string code, CancellationToken cancellationToken);
    Task QueueGrantFromPackMesPaymentAsync(Guid inviteeTenantId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ProcessPendingAsync(DateTimeOffset now, int packMesDays, CancellationToken cancellationToken);
}

public static class ReferralCode
{
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[5];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
