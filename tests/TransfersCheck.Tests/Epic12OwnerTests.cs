using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic12OwnerTests(SqlFixture sql)
{
    [Fact]
    public async Task Owner_sign_in_from_config_lists_service_cards_and_replaces_checkout_token_without_showing_the_full_secret()
    {
        await using var factory = new TransfersApiFactory(sql) { Owner = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var refused = await client.PostAsJsonAsync("/api/owner/sessions", new { email = "wrong@owner.test", password = "password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using var signIn = await client.PostAsJsonAsync("/api/owner/sessions", new { email = "owner@transferscheck.test", password = "owner-password-1" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var cookie = signIn.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("tc_owner=", StringComparison.Ordinal)).Split(';')[0];

        using var services = await Send(client, "/api/owner/services", cookie);
        Assert.Equal(HttpStatusCode.OK, services.StatusCode);
        var cards = await services.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, cards.GetArrayLength());
        Assert.Contains(cards.EnumerateArray(), card => card.GetProperty("card").GetString() == OwnerServiceCards.MercadoPagoPlatform);
        Assert.Contains(cards.EnumerateArray(), card =>
            card.GetProperty("card").GetString() == OwnerServiceCards.WhatsApp
            && card.GetProperty("isSet").GetBoolean()
            && card.GetProperty("lastFour").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);

        using var replace = await Send(client, $"/api/owner/services/{OwnerServiceCards.MercadoPagoPlatform}", cookie, HttpMethod.Put,
            new { secret = "APP_USR-secret-token-9999", expiresAt = (DateTimeOffset?)null });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        var body = await replace.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("9999", body.GetProperty("lastFour").GetString());
        var replaceText = body.ToString();
        Assert.DoesNotContain("APP_USR-secret-token-9999", replaceText, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var token = await scope.ServiceProvider.GetRequiredService<ICheckoutAccessToken>().GetAsync(CancellationToken.None);
        Assert.Equal("APP_USR-secret-token-9999", token);

        using var whatsapp = await Send(client, $"/api/owner/services/{OwnerServiceCards.WhatsApp}", cookie, HttpMethod.Put,
            new { secret = "anything" });
        Assert.Equal(HttpStatusCode.Conflict, whatsapp.StatusCode);

        var ownerApi = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Api", "Owner", "OwnerEndpoints.cs"));
        Assert.Contains("Not Auth0", ownerApi, StringComparison.Ordinal);
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "web", "src", "App.tsx"));
        Assert.Contains("/owner", app, StringComparison.Ordinal);
        Assert.Contains("Panel de servicios", app, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string url, string cookie, HttpMethod? method = null, object? json = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (json is not null)
            request.Content = JsonContent.Create(json);
        return await client.SendAsync(request);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
