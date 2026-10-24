namespace TransfersCheck.Core;

public sealed class CommercialOptions
{
    public const string Section = "Commercial";
    public string Currency { get; set; } = "ARS";
    public int PaidPeriodDays { get; set; } = 30;
    public int WeeklyQueryCap { get; set; } = 500;
    public int WeeklyQueryWarningGap { get; set; } = 50;
}

public sealed class SyncOptions
{
    public const string Section = "Sync";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ButtonCooldown { get; set; } = TimeSpan.FromSeconds(60);
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
    public string AccessToken { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
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
