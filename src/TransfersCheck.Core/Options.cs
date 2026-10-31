namespace TransfersCheck.Core;

public sealed class CommercialOptions
{
    public const string Section = "Commercial";
    public string Currency { get; set; } = "ARS";
    public int PaidPeriodDays { get; set; } = 30;
}

public sealed class SyncOptions
{
    public const string Section = "Sync";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan QuietPollInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan TimerInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ButtonCooldown { get; set; } = TimeSpan.FromSeconds(10);
    public int PageSize { get; set; } = 10;
    public int AlertPointRecordCount { get; set; } = 10;
}

public sealed class OnboardingOptions
{
    public const string Section = "Onboarding";
    public string MercadoPagoVideoUrl { get; set; } = "";
}

public sealed class MercadoPagoOptions
{
    public const string Section = "MercadoPago";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AuthorizationUrl { get; set; } = "";
    public string TokenUrl { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public TimeSpan AttemptLifetime { get; set; } = TimeSpan.FromMinutes(10);
}

public sealed class CheckoutOptions
{
    public const string Section = "Checkout";
    /// <summary>SaaS owner Mercado Pago token for payment links. Stays empty in the repo; story 12.3 fills it outside this build.</summary>
    public string AccessToken { get; set; } = "";
    public string WebhookSecret { get; set; } = ""; public string NotificationSigningKey { get; set; } = ""; public TimeSpan NotificationTolerance { get; set; } = TimeSpan.FromMinutes(10);
    public string NotificationUrl { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
}

public sealed class LoginSessionOptions
{
    public const string Section = "Session";
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);
    public TimeSpan EmailCodeLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan InviteLinkLifetime { get; set; } = TimeSpan.FromHours(1);
    public string SigningKey { get; set; } = "";
    public string CookieName { get; set; } = "tc_session";
}

public sealed class CorsOptions
{
    public const string Section = "Cors";
    public string UiOrigin { get; set; } = "";
}

public sealed class DataProtectionPathOptions
{
    public const string Section = "DataProtection";
    public string KeyPath { get; set; } = "";
}

public sealed class FirebaseOptions
{
    public const string Section = "Firebase";
    public string ProjectId { get; set; } = "";
    public string ServiceAccountPath { get; set; } = "";
    public string MessagingUrl { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string AuthDomain { get; set; } = "";
    public string MessagingSenderId { get; set; } = "";
    public string AppId { get; set; } = "";
    public string VapidKey { get; set; } = "";
}

public sealed class UiOptions
{
    public const string Section = "Ui";
    public string Origin { get; set; } = "";
}

public sealed class Auth0Options
{
    public const string Section = "Auth0";
    /// <summary>Tenant base URL, for example https://your-tenant.auth0.com. Empty keeps the local door (development without a tenant).</summary>
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    /// <summary>Stays in user-secrets or environment (ADR-018). Never committed.</summary>
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    /// <summary>Allowed logout return URL in the Auth0 tenant. Empty falls back to <see cref="UiOptions.Origin"/>.</summary>
    public string LogoutReturnUri { get; set; } = "";
    public TimeSpan AttemptLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority);
}

/// <summary>Product schedules for the two Hangfire jobs (ADR-011). Defaults run daily / every six hours UTC.</summary>
public sealed class HangfireJobsOptions
{
    public const string Section = "HangfireJobs";
    public string FiveDayNoticeCron { get; set; } = "0 12 * * *";
    public string ReferralBonusCron { get; set; } = "0 */6 * * *";
}

/// <summary>
/// Product mail (five-day notice; residual local Código when Auth0 is off). Empty Host keeps <c>NullMailSender</c>.
/// Set via environment or user-secrets — never commit real credentials.
/// </summary>
public sealed class MailOptions
{
    public const string Section = "Mail";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public bool UseSsl { get; set; } = true;

    public bool Enabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>Owner microsite door (ADR-014 / story 12.1). Supplied only from server configuration — no public signup.</summary>
public sealed class OwnerOptions
{
    public const string Section = "Owner";
    public string Email { get; set; } = "";
    /// <summary>Plain password for the first account; stays in user-secrets or environment.</summary>
    public string Password { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>
/// Help copilot adapters (ADR-017). Keys stay empty in the repo — set via user-secrets or environment.
/// Provider names and model thresholds remain unset product choices; placeholders only.
/// </summary>
public sealed class HelpCopilotOptions
{
    public const string Section = "HelpCopilot";
    public string JevBaseUrl { get; set; } = "https://api.typesafe.ai/v1/systemone";
    public string JevApiKey { get; set; } = "";
    public string JevModel { get; set; } = "";
    public string GenerativeProvider { get; set; } = "";
    public string GenerativeApiKey { get; set; } = "";
    public string GenerativeModel { get; set; } = "";
    public string GenerativeBaseUrl { get; set; } = "";
    public string FallbackProvider { get; set; } = "";
    public string FallbackApiKey { get; set; } = "";
    public string FallbackModel { get; set; } = "";
    public string FallbackBaseUrl { get; set; } = "";
    public int MaxQuestionLength { get; set; } = 500;
    public string HelpCenterPath { get; set; } = "/ayuda";
    /// <summary>Folder of markdown help pages relative to the content root, or an absolute path (ADR-016).</summary>
    public string CorpusPath { get; set; } = "";

    public bool JevConfigured => !string.IsNullOrWhiteSpace(JevApiKey);
    public bool GenerativeConfigured => !string.IsNullOrWhiteSpace(GenerativeApiKey) && !string.IsNullOrWhiteSpace(GenerativeBaseUrl);
}
