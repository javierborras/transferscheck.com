namespace TransfersCheck.Core;

public sealed record IncomingTransfer(
    string ExternalId,
    string PayerName,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    string Status,
    string TypeCode);

public sealed record TransferChange(Guid TransferId, string ExternalId, string PayerName, decimal Amount, DateTimeOffset OccurredAt, string Status, bool NewlyArrived);

public sealed class RateLimitedException : Exception
{
    public RateLimitedException() : base("Mercado Pago rate limit.") { }
}

public sealed record TokenSet(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string ExternalAccountId);

public interface ITransferSource
{
    string SourceCode { get; }
    Task<IReadOnlyList<IncomingTransfer>> ReadIncomingAsync(TokenSet tokens, int pageSize, CancellationToken cancellationToken);
}

public interface IMerchantAuthorizer
{
    string BuildAuthorizationUrl(string state, string codeChallenge);
    Task<TokenSet> ExchangeAsync(string code, string codeVerifier, CancellationToken cancellationToken);
    Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}

public interface IEntitlement
{
    bool AllowsSyncAndPush(DateTimeOffset? accessUntil, DateTimeOffset now);
    DateTimeOffset StartTrial(DateTimeOffset connectedAt, int trialDays);
    DateTimeOffset Extend(DateTimeOffset? accessUntil, DateTimeOffset paidAt, int paidPeriodDays);
}

public sealed class Entitlement : IEntitlement
{
    public bool AllowsSyncAndPush(DateTimeOffset? accessUntil, DateTimeOffset now)
        => accessUntil is { } until && until > now;

    public DateTimeOffset StartTrial(DateTimeOffset connectedAt, int trialDays)
        => connectedAt.AddDays(trialDays);

    public DateTimeOffset Extend(DateTimeOffset? accessUntil, DateTimeOffset paidAt, int paidPeriodDays)
    {
        var start = accessUntil is { } until && until > paidAt ? until : paidAt;
        return start.AddDays(paidPeriodDays);
    }
}

public interface ITokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string payload);
}

public interface IMerchantStore
{
    Task AddMerchantAsync(Merchant merchant, CancellationToken cancellationToken);
    Task<Merchant?> FindMerchantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<LoginAccount?> FindAccountByEmailAsync(string email, CancellationToken cancellationToken);
    Task<LoginAccount?> FindAccountByAuth0SubjectAsync(string subject, CancellationToken cancellationToken);
    Task<LoginAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LoginAccount>> ListAccountsAsync(CancellationToken cancellationToken);
    Task<int> CountAccountsAsync(CancellationToken cancellationToken);
    Task AddAccountAsync(LoginAccount account, CancellationToken cancellationToken);

    Task AddSessionAsync(LoginSession session, CancellationToken cancellationToken);
    Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task AddOAuthAttemptAsync(OAuthAttempt attempt, CancellationToken cancellationToken);
    Task<OAuthAttempt?> FindOAuthAttemptAsync(string state, CancellationToken cancellationToken);
    Task DeleteOAuthAttemptAsync(OAuthAttempt attempt, CancellationToken cancellationToken);

    Task<MerchantConnection?> FindConnectionAsync(CancellationToken cancellationToken);
    Task<bool> ExternalAccountLinkedElsewhereAsync(string externalAccountId, Guid tenantId, CancellationToken cancellationToken);
    Task AddConnectionAsync(MerchantConnection connection, CancellationToken cancellationToken);

    Task<IReadOnlyList<Transfer>> ListTransfersAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransferChange>> UpsertTransfersAsync(string source, IReadOnlyList<IncomingTransfer> incoming, CancellationToken cancellationToken);

    Task AddPushDeviceAsync(PushDevice device, CancellationToken cancellationToken);
    Task<bool> HasPushDeviceAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PushDevice>> ListApprovedPushDevicesAsync(CancellationToken cancellationToken);
    Task DeletePushDevicesForAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<bool> PaymentAlreadyAppliedAsync(string paymentId, CancellationToken cancellationToken);
    Task AddProcessedPaymentAsync(ProcessedPayment payment, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListConnectedTenantIdsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListStoredTypeCodesAsync(CancellationToken cancellationToken);
}

public interface IPollLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}

public interface IPushSender
{
    Task SendArrivedAsync(string token, Guid transferId, string payerName, decimal amount, string currency, CancellationToken cancellationToken);
}

public interface ICheckout
{
    /// <summary>Creates a prepaid Mercado Pago payment link (not preapproval). Owner AccessToken may be empty until story 12.3.</summary>
    Task<string> CreatePaymentLinkAsync(Guid tenantId, int amount, string currency, string title, CancellationToken cancellationToken);
    Task<string?> ReadApprovedPaymentAsync(string paymentId, CancellationToken cancellationToken);
}

/// <summary>Named service cards on the owner microsite (ADR-012). WhatsApp has no secret.</summary>
public static class OwnerServiceCards
{
    public const string MercadoPagoPlatform = "mercadopago_platform";
    public const string Mail = "mail";
    public const string BrowserPush = "browser_push";
    public const string WhatsApp = "whatsapp";

    public static readonly IReadOnlyList<string> All =
    [
        MercadoPagoPlatform,
        Mail,
        BrowserPush,
        WhatsApp
    ];
}

public sealed record OwnerSecretStatus(string Card, bool IsSet, string? LastFour, DateTimeOffset? ExpiresAt);

public interface IOwnerSecretStore
{
    Task<IReadOnlyList<OwnerSecretStatus>> ListAsync(CancellationToken cancellationToken);
    Task<string?> GetPlainAsync(string card, CancellationToken cancellationToken);
    Task SetAsync(string card, string plainSecret, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
}

/// <summary>Checkout Pro token: owner panel override, then <c>Checkout:AccessToken</c> config.</summary>
public interface ICheckoutAccessToken
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Who Auth0 says the commerce admin is. Only a verified email may open a session.</summary>
public sealed record IdentityClaims(string Subject, string Email, bool EmailVerified);

public interface IIdentityProvider
{
    string BuildAuthorizationUrl(string state, string codeChallenge, string nonce, bool signUp, string? loginHint);
    /// <summary>Auth0 federated logout URL so Cerrar sesión also ends the Universal Login SSO session.</summary>
    string BuildLogoutUrl(string returnTo);
    Task<IdentityClaims> ExchangeAsync(string code, string codeVerifier, string nonce, CancellationToken cancellationToken);
}
