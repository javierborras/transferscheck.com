using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class MercadoPagoTransferSource(HttpClient http, IOptions<MercadoPagoOptions> options, IMercadoPagoCallLog? calls = null) : ITransferSource, ITransferQuerySource
{
    public const string Code = "mercadopago";
    public string SourceCode => Code;

    public async Task<IReadOnlyList<IncomingTransfer>> ReadIncomingAsync(TokenSet tokens, int pageSize, CancellationToken cancellationToken)
        => (await QueryAsync(tokens, null, null, 0, pageSize, cancellationToken)).Items;

    public async Task<TransferPage> QueryAsync(TokenSet tokens, DateTimeOffset? from, DateTimeOffset? to, int offset, int limit, CancellationToken cancellationToken)
    {
        var root = options.Value.ApiBaseUrl.TrimEnd('/');
        var url = $"{root}/v1/payments/search?sort=date_created&criteria=desc&limit={limit}&offset={offset}";
        if (from is not null || to is not null) {
            url += "&range=date_created";
            if (from is { } start) url += "&begin_date=" + Uri.EscapeDataString(start.ToString("o"));
            if (to is { } end) url += "&end_date=" + Uri.EscapeDataString(end.ToString("o"));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (calls is not null)
            await calls.RecordAsync(cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode == 429 || ((int)response.StatusCode >= 400 && body.Contains("2001", StringComparison.Ordinal)))
            throw new RateLimitedException();
        response.EnsureSuccessStatusCode();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Mercado Pago did not return a payment list.", exception);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new HttpRequestException("Mercado Pago did not return a payment list.");

            var transfers = new List<IncomingTransfer>();
            var rawCount = 0;
            foreach (var item in results.EnumerateArray())
            {
                rawCount++;
                if (TryParsePayment(item, out var transfer))
                    transfers.Add(transfer);
            }

            var next = rawCount == limit ? offset + limit : (int?)null;
            if (doc.RootElement.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("total", out var total)
                && total.TryGetInt64(out var totalCount)
                && offset + rawCount >= totalCount)
                next = null;
            return new TransferPage(transfers, next);
        }
    }

    public static IncomingTransfer ParsePayment(JsonElement item)
        => TryParsePayment(item, out var transfer)
            ? transfer
            : throw new InvalidOperationException("Payment is missing id, amount, or date_created.");

    public static bool TryParsePayment(JsonElement item, out IncomingTransfer transfer)
    {
        transfer = null!;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) || id.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (!TryAmount(item, out var amount))
            return false;
        if (!item.TryGetProperty("date_created", out var created) || !created.TryGetDateTimeOffset(out var occurredAt))
            return false;
        var collectorId = ReadScalar(item, "collector_id");
        var operationType = ReadString(item, "operation_type");
        var paymentMethodId = ReadString(item, "payment_method_id");
        var typeCode = ReadString(item, "payment_type_id");

        var hasPayer = item.TryGetProperty("payer", out var payer) && payer.ValueKind == JsonValueKind.Object;
        var payerId = hasPayer ? ReadScalar(payer, "id") : "";
        // Mercado Pago sets payer == collector_id on operation_type == "account_fund" (CVU pay-ins).
        var payerIsCollector = !string.IsNullOrEmpty(collectorId)
            && !string.IsNullOrEmpty(payerId)
            && string.Equals(collectorId, payerId, StringComparison.Ordinal);

        var first = !payerIsCollector && hasPayer ? ReadString(payer, "first_name") : "";
        var last = !payerIsCollector && hasPayer ? ReadString(payer, "last_name") : "";
        var name = $"{first} {last}".Trim();

        var payerEmail = !payerIsCollector && hasPayer ? ReadString(payer, "email") : "";
        var payerDoc = "";
        if (!payerIsCollector && hasPayer && payer.TryGetProperty("identification", out var idObj) && idObj.ValueKind == JsonValueKind.Object)
        {
            var idType = ReadString(idObj, "type");
            var idNum = ReadScalar(idObj, "number");
            payerDoc = string.IsNullOrEmpty(idNum) ? "" : string.IsNullOrEmpty(idType) ? idNum : $"{idType} {idNum}";
        }

        var coelsaId = "";
        var bankTransferId = "";
        var originBank = "";
        var originAccount = "";

        if (item.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Object)
        {
            coelsaId = FirstNonEmpty(ReadScalar(details, "transaction_id"), ReadScalar(details, "payment_method_reference_id"));
            bankTransferId = ReadScalar(details, "bank_transfer_id");
        }

        if (item.TryGetProperty("point_of_interaction", out var poi) && poi.ValueKind == JsonValueKind.Object)
        {
            if (poi.TryGetProperty("transaction_data", out var txData) && txData.ValueKind == JsonValueKind.Object)
            {
                coelsaId = FirstNonEmpty(ReadScalar(txData, "e2e_id"), ReadScalar(txData, "transaction_id"), coelsaId);
                bankTransferId = FirstNonEmpty(ReadScalar(txData, "bank_transfer_id"), bankTransferId);

                if (txData.TryGetProperty("bank_info", out var bankInfo) && bankInfo.ValueKind == JsonValueKind.Object
                    && bankInfo.TryGetProperty("payer", out var bankPayer) && bankPayer.ValueKind == JsonValueKind.Object)
                {
                    originBank = ReadString(bankPayer, "long_name");
                    originAccount = FirstNonEmpty(ReadScalar(bankPayer, "account_id"), ReadString(bankPayer, "account_alias"));
                    if (string.IsNullOrEmpty(name))
                        name = ReadString(bankPayer, "account_holder_name");
                    if (string.IsNullOrEmpty(payerDoc) && bankPayer.TryGetProperty("identification", out var bankId) && bankId.ValueKind == JsonValueKind.Object)
                    {
                        var bType = ReadString(bankId, "type");
                        var bNum = ReadScalar(bankId, "number");
                        payerDoc = string.IsNullOrEmpty(bNum) ? "" : string.IsNullOrEmpty(bType) ? bNum : $"{bType} {bNum}";
                    }
                }
            }
        }

        var description = ReadString(item, "description");
        if (string.Equals(description, "Bank Transfer", StringComparison.OrdinalIgnoreCase))
            description = "";

        DateTimeOffset? approvedAt = item.TryGetProperty("date_approved", out var approvedProp)
            && approvedProp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(approvedProp.GetString(), out var parsedApproved)
            ? parsedApproved
            : null;

        var currency = "ARS";
        if (item.TryGetProperty("currency_id", out var currencyProp) && currencyProp.ValueKind == JsonValueKind.String)
            currency = currencyProp.GetString() ?? "ARS";

        transfer = new IncomingTransfer(
            id.ToString(),
            name,
            amount,
            currency,
            occurredAt,
            ReadString(item, "status"),
            typeCode,
            approvedAt,
            ReadString(item, "status_detail"),
            operationType,
            paymentMethodId,
            coelsaId,
            bankTransferId,
            payerDoc,
            payerEmail,
            originBank,
            originAccount,
            description);
        return true;
    }

    private static bool TryAmount(JsonElement item, out decimal amount)
    {
        amount = 0;
        if (!item.TryGetProperty("transaction_amount", out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out amount))
            return true;
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out amount);
    }

    private static string ReadString(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "").Trim() : "";

    private static string ReadScalar(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var val)) return "";
        return val.ValueKind switch
        {
            JsonValueKind.String => (val.GetString() ?? "").Trim(),
            JsonValueKind.Number => val.GetRawText(),
            _ => ""
        };
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}

public sealed class MercadoPagoCheckout(
    HttpClient http,
    IOptions<CheckoutOptions> checkout,
    IOptions<MercadoPagoOptions> mercadoPago,
    IOptions<LoginSessionOptions> session,
    ICheckoutAccessToken accessToken,
    IMercadoPagoCallLog? calls = null) : ICheckout
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
            notification_url = CheckoutNotificationSignature.BuildNotificationUrl(checkout.Value.NotificationUrl, reference, session.Value.SigningKey),
            back_urls = new { success = checkout.Value.ReturnUrl }
        });
        if (calls is not null)
            await calls.RecordAsync(cancellationToken);
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
        if (calls is not null)
            await calls.RecordAsync(cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        // The official webhook simulator uses a payment ID that does not exist.
        // No payment means no access to apply; authentication/outage errors still fail for retry.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
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
