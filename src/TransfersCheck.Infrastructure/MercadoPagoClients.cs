using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class MercadoPagoTransferSource(HttpClient http, IOptions<MercadoPagoOptions> options) : ITransferSource
{
    public const string Code = "mercadopago";
    public string SourceCode => Code;

    public async Task<IReadOnlyList<IncomingTransfer>> ReadIncomingAsync(TokenSet tokens, int pageSize, CancellationToken cancellationToken)
    {
        var root = options.Value.ApiBaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{root}/v1/payments/search?sort=date_created&criteria=desc&limit={pageSize}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode == 429 || body.Contains("2001", StringComparison.Ordinal))
            throw new RateLimitedException();
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var transfers = new List<IncomingTransfer>();
        foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            var payer = item.GetProperty("payer");
            var first = payer.TryGetProperty("first_name", out var given) ? given.GetString() ?? "" : "";
            var last = payer.TryGetProperty("last_name", out var family) ? family.GetString() ?? "" : "";
            var name = $"{first} {last}".Trim();
            transfers.Add(new IncomingTransfer(
                item.GetProperty("id").ToString(),
                name,
                item.GetProperty("transaction_amount").GetDecimal(),
                item.TryGetProperty("currency_id", out var currency) ? currency.GetString() ?? "ARS" : "ARS",
                item.GetProperty("date_created").GetDateTimeOffset(),
                item.GetProperty("status").GetString() ?? "",
                item.GetProperty("payment_type_id").GetString() ?? ""));
        }

        return transfers;
    }
}

public sealed class MercadoPagoCheckout(
    HttpClient http,
    IOptions<CheckoutOptions> checkout,
    IOptions<MercadoPagoOptions> mercadoPago,
    ICheckoutAccessToken accessToken) : ICheckout
{
    public async Task<string> CreatePaymentLinkAsync(string reference, int amount, string currency, string title, CancellationToken cancellationToken)
    {
        var root = mercadoPago.Value.ApiBaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{root}/checkout/preferences");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken.GetAsync(cancellationToken));
        request.Content = JsonContent.Create(new
        {
            items = new[]
            {
                new { title, quantity = 1, currency_id = currency, unit_price = amount }
            },
            external_reference = reference,
            notification_url = checkout.Value.NotificationUrl,
            back_urls = new { success = checkout.Value.ReturnUrl }
        });
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetProperty("init_point").GetString() ?? "";
    }

    public async Task<ApprovedCheckoutPayment?> ReadApprovedPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        var root = mercadoPago.Value.ApiBaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{root}/v1/payments/{Uri.EscapeDataString(paymentId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken.GetAsync(cancellationToken));
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.GetProperty("status").GetString() != "approved")
            return null;
        return new ApprovedCheckoutPayment(
            doc.RootElement.GetProperty("external_reference").GetString() ?? "",
            doc.RootElement.GetProperty("transaction_amount").GetDecimal(),
            doc.RootElement.GetProperty("currency_id").GetString() ?? "");
    }
}

public sealed class SqlPollLock(string connectionString) : IPollLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = N'TransfersCheck.Poll',
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = 0;
            SELECT @result;
            """;
        var result = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? -1);
        if (result < 0)
        {
            await connection.DisposeAsync();
            return null;
        }

        return new HeldLock(connection);
    }

    private sealed class HeldLock(Microsoft.Data.SqlClient.SqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var release = connection.CreateCommand();
                release.CommandText = "EXEC sp_releaseapplock @Resource = N'TransfersCheck.Poll', @LockOwner = N'Session';";
                await release.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
