using Microsoft.Extensions.Configuration;
using TransfersCheck.Api.Configuration;

namespace TransfersCheck.Tests;

public sealed class ProductionConfigurationTests
{
    private static Dictionary<string, string?> Valid() => new() {
        ["ConnectionStrings:TransfersCheck"] = "Server=db.example.com;Database=production;User Id=app;Password=secret;Encrypt=True;TrustServerCertificate=False",
        ["Session:SigningKey"] = new string('x', 48), ["DataProtection:KeyPath"] = "App_Data/keys",
        ["Ui:Origin"] = "https://transferscheck.com", ["Cors:UiOrigin"] = "https://transferscheck.com",
        ["Auth0:Authority"] = "https://identity.example.com", ["Auth0:ClientId"] = "real-client", ["Auth0:ClientSecret"] = "secret-value",
        ["Auth0:RedirectUri"] = "https://api.example.com/api/auth0/callback",
        ["MercadoPago:ClientId"] = "real-client", ["MercadoPago:ClientSecret"] = "secret-value",
        ["MercadoPago:AuthorizationUrl"] = "https://auth.mercadopago.com.ar/authorization", ["MercadoPago:TokenUrl"] = "https://api.mercadopago.com/oauth/token",
        ["MercadoPago:ApiBaseUrl"] = "https://api.mercadopago.com", ["MercadoPago:RedirectUri"] = "https://api.example.com/api/connections/mercadopago/callback"
    };
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Production_accepts_real_endpoints_and_validated_sql_tls()
        => Assert.Empty(ProductionConfiguration.Errors(Config(Valid()), "Production"));

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Local_environments_do_not_require_production_services(string environment)
        => Assert.Empty(ProductionConfiguration.Errors(Config([]), environment));

    [Theory]
    [InlineData("Auth0:Authority", "http://localhost:5099/auth0")]
    [InlineData("MercadoPago:ApiBaseUrl", "http://127.0.0.1:5099")]
    [InlineData("Firebase:ProjectId", "mock")]
    [InlineData("MercadoPago:ClientSecret", "mock-secret")]
    [InlineData("MercadoPago:RedirectUri", "https://api.example.com/connections/mercadopago/callback")]
    [InlineData("Checkout:WebhookSecret", "test-webhook-secret")]
    public void Production_rejects_simulators_and_legacy_callback_paths(string key, string value)
    {
        var values = Valid(); values[key] = value;
        var errors = ProductionConfiguration.Errors(Config(values), "Production");
        Assert.Contains(errors, error => error.Contains(key, StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("secret-value", StringComparison.Ordinal) || error.Contains("mock-secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_production_config_fails_before_any_database_service_starts()
        => Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.RequireValid(Config([]), "Production"));
}
