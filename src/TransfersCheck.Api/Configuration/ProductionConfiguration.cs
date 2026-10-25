using System.Text;
using Microsoft.Data.SqlClient;

namespace TransfersCheck.Api.Configuration;

/// <summary>Validate before creating SQL/Hangfire services. Errors contain key names, never values.</summary>
public static class ProductionConfiguration
{
    public static IReadOnlyList<string> Errors(IConfiguration config, string environment)
    {
        if (environment is "Development" or "Test") return [];
        var errors = new List<string>();
        foreach (var key in new[] { "Auth0:ClientId", "Auth0:ClientSecret", "MercadoPago:ClientId", "MercadoPago:ClientSecret", "DataProtection:KeyPath" })
            if (string.IsNullOrWhiteSpace(config[key])) errors.Add($"{key} is required outside Development/Test.");
        var signingKey = config["Session:SigningKey"] ?? "";
        if (Encoding.UTF8.GetByteCount(signingKey) < 32 || signingKey.StartsWith("test-signing-", StringComparison.Ordinal) || signingKey.StartsWith("e2e-signing-", StringComparison.Ordinal))
            errors.Add("Session:SigningKey requires a production key of at least 32 bytes.");
        if (!string.IsNullOrWhiteSpace(config["Checkout:WebhookSecret"]))
            errors.Add("Checkout:WebhookSecret is only for Development/Test and cannot be set in production (ADR-022).");

        foreach (var key in new[] { "Ui:Origin", "Cors:UiOrigin", "Auth0:Authority", "Auth0:RedirectUri", "MercadoPago:AuthorizationUrl", "MercadoPago:TokenUrl", "MercadoPago:ApiBaseUrl", "MercadoPago:RedirectUri" })
            ValidateUrl(key, true);
        foreach (var key in new[] { "Auth0:LogoutReturnUri", "Checkout:NotificationUrl", "Checkout:ReturnUrl", "Firebase:MessagingUrl" })
            ValidateUrl(key, false);
        ValidateCallback("Auth0:RedirectUri", "/api/auth0/callback");
        ValidateCallback("MercadoPago:RedirectUri", "/api/connections/mercadopago/callback");
        if (config["Firebase:ProjectId"] == "mock") errors.Add("Firebase:ProjectId cannot select the simulator in production.");
        foreach (var key in new[] { "Auth0:ClientId", "Auth0:ClientSecret", "MercadoPago:ClientId", "MercadoPago:ClientSecret", "Checkout:AccessToken", "Checkout:NotificationSigningKey" })
            if ((config[key] ?? "").StartsWith("mock-", StringComparison.OrdinalIgnoreCase)) errors.Add($"{key} cannot use a mock credential.");
        try {
            var sql = new SqlConnectionStringBuilder(config.GetConnectionString("TransfersCheck") ?? "");
            var server = sql.DataSource.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(sql.InitialCatalog) || server.Contains("localhost") || server.Contains("127.0.0.1") || server.Contains("localdb") || server.Contains("sqlexpress") || server is "." or "(local)")
                errors.Add("ConnectionStrings:TransfersCheck requires an explicit production database.");
            if (sql.TrustServerCertificate || sql.Encrypt == SqlConnectionEncryptOption.Optional)
                errors.Add("ConnectionStrings:TransfersCheck requires encryption and certificate validation.");
        } catch (ArgumentException) { errors.Add("ConnectionStrings:TransfersCheck is not a valid SQL Server connection string."); }
        return errors;

        void ValidateUrl(string key, bool required) {
            var value = config[key];
            if (!required && string.IsNullOrWhiteSpace(value)) return;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains("/mock/", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{key} must be an HTTPS production endpoint, not a local/mock endpoint.");
        }
        void ValidateCallback(string key, string path) {
            if (Uri.TryCreate(config[key], UriKind.Absolute, out var uri) && uri.AbsolutePath != path)
                errors.Add($"{key} must use the API callback path {path}.");
        }
    }

    public static void RequireValid(IConfiguration config, string environment)
    {
        var errors = Errors(config, environment);
        if (errors.Count > 0) throw new InvalidOperationException("Production configuration is incomplete: " + string.Join(" ", errors));
    }
}
