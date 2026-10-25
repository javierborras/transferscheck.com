using System.Net;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Tests;

public sealed class MercadoPagoResponseTests
{
    [Theory]
    [InlineData("")]
    [InlineData(",\"payer\":null")]
    [InlineData(",\"payer\":{}")]
    public async Task Missing_payer_keeps_the_payment_without_inventing_a_name(string payer)
    {
        var json = "{\"results\":[{\"id\":123,\"transaction_amount\":4200.50,\"currency_id\":\"ARS\",\"date_created\":\"2026-10-25T10:00:00Z\",\"status\":\"approved\",\"payment_type_id\":\"bank_transfer\"" + payer + "}],\"paging\":{\"total\":1}}";
        using var http = new HttpClient(new ResponseHandler(json));
        var source = new MercadoPagoTransferSource(http, Options.Create(new MercadoPagoOptions { ApiBaseUrl = "https://provider.example" }));
        var page = await source.QueryAsync(new TokenSet("test", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None);
        var payment = Assert.Single(page.Items);
        Assert.Equal("", payment.PayerName);
        Assert.Equal(4200.50m, payment.Amount);
        Assert.Null(page.NextOffset);
    }

    private sealed class ResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
