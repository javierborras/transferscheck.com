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

    [Fact]
    public async Task Cvu_account_fund_extracts_coelsa_and_suppresses_self_collector_payer_data()
    {
        const string json = """
        {
          "results": [{
            "id": 152831196973,
            "date_created": "2026-04-13T21:18:19.000-04:00",
            "date_approved": "2026-04-13T21:18:22.000-04:00",
            "operation_type": "account_fund",
            "payment_method_id": "cvu",
            "payment_type_id": "bank_transfer",
            "status": "approved",
            "status_detail": "accredited",
            "currency_id": "ARS",
            "transaction_amount": 38910,
            "collector_id": 22403227,
            "payer": {
              "id": "22403227",
              "email": "collector@self.test",
              "identification": { "type": "CUIT", "number": "20267565856" }
            },
            "transaction_details": {
              "transaction_id": "76V4MR2Z8O30ZDY09DEZOL",
              "bank_transfer_id": 129581799522
            },
            "point_of_interaction": {
              "type": "PSP_TRANSFER",
              "sub_type": "INTER_PSP",
              "transaction_data": {
                "e2e_id": "76V4MR2Z8O30ZDY09DEZOL",
                "transaction_id": "76V4MR2Z8O30ZDY09DEZOL"
              }
            }
          }],
          "paging": { "total": 1 }
        }
        """;
        using var http = new HttpClient(new ResponseHandler(json));
        var source = new MercadoPagoTransferSource(http, Options.Create(new MercadoPagoOptions { ApiBaseUrl = "https://provider.example" }));
        var page = await source.QueryAsync(new TokenSet("test", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None);
        var payment = Assert.Single(page.Items);
        Assert.Equal("152831196973", payment.ExternalId);
        Assert.Equal("account_fund", payment.OperationType);
        Assert.Equal("cvu", payment.PaymentMethodId);
        Assert.Equal("accredited", payment.StatusDetail);
        Assert.Equal("76V4MR2Z8O30ZDY09DEZOL", payment.CoelsaId);
        Assert.Equal("129581799522", payment.BankTransferId);
        Assert.Equal("", payment.PayerDocument);
        Assert.Equal("", payment.PayerEmail);
        Assert.NotNull(payment.ApprovedAt);
    }

    [Fact]
    public async Task Intra_mp_transfer_extracts_sender_cuit_email_description_and_coelsa()
    {
        const string json = """
        {
          "results": [{
            "id": 151009996815,
            "date_created": "2026-03-27T11:25:15.000-04:00",
            "date_approved": "2026-03-27T11:25:16.000-04:00",
            "operation_type": "money_transfer",
            "payment_method_id": "account_money",
            "payment_type_id": "account_money",
            "status": "approved",
            "status_detail": "accredited",
            "currency_id": "ARS",
            "description": "Varios",
            "transaction_amount": 36000,
            "collector_id": 22403227,
            "payer": {
              "id": "1331850865",
              "email": "sender@mp.test",
              "identification": { "type": "CUIL", "number": "27302646636" }
            },
            "point_of_interaction": {
              "type": "PSP_TRANSFER",
              "sub_type": "INTRA_PSP",
              "transaction_data": {
                "e2e_id": "82433931797",
                "transaction_id": "151009996810"
              }
            }
          }],
          "paging": { "total": 1 }
        }
        """;
        using var http = new HttpClient(new ResponseHandler(json));
        var source = new MercadoPagoTransferSource(http, Options.Create(new MercadoPagoOptions { ApiBaseUrl = "https://provider.example" }));
        var page = await source.QueryAsync(new TokenSet("test", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None);
        var payment = Assert.Single(page.Items);
        Assert.Equal("151009996815", payment.ExternalId);
        Assert.Equal("money_transfer", payment.OperationType);
        Assert.Equal("account_money", payment.PaymentMethodId);
        Assert.Equal("CUIL 27302646636", payment.PayerDocument);
        Assert.Equal("sender@mp.test", payment.PayerEmail);
        Assert.Equal("Varios", payment.Description);
        Assert.Equal("82433931797", payment.CoelsaId);
    }

    [Fact]
    public async Task A_payment_missing_amount_or_date_does_not_drop_the_rest_of_the_page()
    {
        const string json = """
        {
          "results": [
            {"id": 1, "date_created": "2026-10-25T10:00:00Z", "status": "approved", "payment_type_id": "bank_transfer"},
            {"id": 2, "transaction_amount": null, "date_created": "2026-10-25T11:00:00Z", "status": "approved"},
            {"id": 3, "transaction_amount": "1500.50", "date_created": "2026-10-25T12:00:00Z", "status": "pending", "currency_id": "ARS"}
          ],
          "paging": {"total": 3}
        }
        """;
        using var http = new HttpClient(new ResponseHandler(json));
        var source = new MercadoPagoTransferSource(http, Options.Create(new MercadoPagoOptions { ApiBaseUrl = "https://provider.example" }));
        var page = await source.QueryAsync(new TokenSet("test", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None);
        var payment = Assert.Single(page.Items);
        Assert.Equal("3", payment.ExternalId);
        Assert.Equal(1500.50m, payment.Amount);
        Assert.Equal("pending", payment.Status);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public async Task Unauthorized_search_reports_401_so_the_sync_can_tell_it_from_an_outage()
    {
        using var http = new HttpClient(new ResponseHandler("{\"message\":\"unauthorized\"}", HttpStatusCode.Unauthorized));
        var source = new MercadoPagoTransferSource(http, Options.Create(new MercadoPagoOptions { ApiBaseUrl = "https://provider.example" }));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            source.QueryAsync(new TokenSet("expired", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Rejected_token_exchange_is_not_a_generic_failure_and_does_not_echo_the_body()
    {
        const string body = "{\"error\":\"invalid_grant\",\"refresh_token\":\"should-not-leak\"}";
        using var http = new HttpClient(new ResponseHandler(body, HttpStatusCode.BadRequest));
        var authorizer = new MercadoPagoAuthorizer(http, Options.Create(new MercadoPagoOptions { TokenUrl = "https://provider.example/oauth/token" }));
        var exception = await Assert.ThrowsAsync<MerchantAuthorizationRejectedException>(() => authorizer.RefreshAsync("stale", CancellationToken.None));
        Assert.DoesNotContain("should-not-leak", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid_grant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_endpoint_outage_stays_a_provider_failure_without_the_response_body()
    {
        using var http = new HttpClient(new ResponseHandler("upstream-secret-body", HttpStatusCode.ServiceUnavailable));
        var authorizer = new MercadoPagoAuthorizer(http, Options.Create(new MercadoPagoOptions { TokenUrl = "https://provider.example/oauth/token" }));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => authorizer.RefreshAsync("stale", CancellationToken.None));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream-secret-body", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_without_results_is_a_provider_failure()
    {
        using var http = new HttpClient(new ResponseHandler("{\"message\":\"invalid_token\"}"));
        var source = new MercadoPagoTransferSource(http, Options.Create(new MercadoPagoOptions { ApiBaseUrl = "https://provider.example" }));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.QueryAsync(new TokenSet("test", "", DateTimeOffset.UtcNow.AddHours(1), ""), null, null, 0, 10, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Checkout_ignores_only_missing_payments_and_preserves_provider_errors(HttpStatusCode status)
    {
        using var http = new HttpClient(new ResponseHandler("{}", status));
        var checkout = new MercadoPagoCheckout(http, Options.Create(new CheckoutOptions()),
            Options.Create(new MercadoPagoOptions { ApiBaseUrl = "https://provider.example" }),
            Options.Create(new LoginSessionOptions()), new TestToken());
        if (status == HttpStatusCode.NotFound)
            Assert.Null(await checkout.ReadApprovedPaymentAsync("123456", CancellationToken.None));
        else
            await Assert.ThrowsAsync<HttpRequestException>(() => checkout.ReadApprovedPaymentAsync("123456", CancellationToken.None));
    }

    private sealed class TestToken : ICheckoutAccessToken
    {
        public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult("test-token");
    }

    private sealed class ResponseHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
    }
}
