using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class OwnerCheckoutStatusTests(SqlFixture sql)
{
    [Fact]
    public async Task OAuth_client_secret_does_not_mark_checkout_token_as_configured()
    {
        await using var factory = new TransfersApiFactory(sql);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<CheckoutOptions>(options => options.AccessToken = "");
            services.PostConfigure<MercadoPagoOptions>(options => options.ClientSecret = "oauth-secret-only");
        }));
        using var scope = app.Services.CreateScope();
        var services = await scope.ServiceProvider.GetRequiredService<IOwnerSecretStore>().ListAsync(CancellationToken.None);
        Assert.False(services.Single(card => card.Card == OwnerServiceCards.MercadoPagoPlatform).IsSet);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ICheckoutAccessToken>().GetAsync(CancellationToken.None));
    }
}
