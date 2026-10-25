using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace TransfersCheck.MockServer;

public static class MockState
{
    static MockState() => SeedPayments();
    public static readonly ConcurrentDictionary<string, string> Challenges = new();
    public static readonly ConcurrentDictionary<string, string> UsedCodes = new();
    public static readonly ConcurrentDictionary<string, string> RefreshUsers = new();
    public static readonly List<JsonElement> Payments = [];
    public static readonly List<string> SentPushes = [];
    public static string RateLimit = "";
    public static long UserId = 1001;
    public static int RefreshCount;
    public static int SearchCount;
    public static string LastCheckoutAuthorization = "";
    public static string LastCheckoutBody = "";
    public static string LastPushAuthorization = "";
    public static readonly ConcurrentDictionary<string, string> CheckoutTenants = new();
    public static readonly ConcurrentDictionary<string, decimal> CheckoutAmounts = new();

    public static void Reset()
    {
        Challenges.Clear();
        UsedCodes.Clear();
        RefreshUsers.Clear();
        Payments.Clear();
        SentPushes.Clear();
        CheckoutTenants.Clear();
        CheckoutAmounts.Clear();
        Auth0Mock.Reset();
        RateLimit = "";
        UserId = 1001;
        RefreshCount = 0;
        SearchCount = 0;
        LastCheckoutAuthorization = "";
        LastCheckoutBody = "";
        LastPushAuthorization = "";
        SeedPayments();
    }

    public static void SeedPayments()
    {
        Payments.Clear();
        for (var i = 1; i <= 8; i++)
            Payments.Add(Payment(i, "approved", "bank_transfer", 1000m + i, $"Persona {i}"));
        Payments.Add(Payment(9, "pending", "account_money", 50m, "Pendiente Uno"));
        Payments.Add(Payment(10, "rejected", "account_money", 75m, "Rechazado Dos"));
    }

    public static JsonElement Payment(int id, string status, string type, decimal amount, string name)
    {
        var json = JsonSerializer.Serialize(new
        {
            id,
            date_created = DateTimeOffset.UtcNow.AddMinutes(-id).ToString("o"),
            transaction_amount = amount,
            currency_id = "ARS",
            status,
            payment_type_id = type,
            payer = new
            {
                first_name = name,
                last_name = string.IsNullOrWhiteSpace(name) ? "" : "Perez",
                email = $"p{id}@counter.test"
            }
        });
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

public static class MockEndpoints
{
    public static void Map(WebApplication app)
    {
        Auth0Mock.Map(app);
        app.MapGet("/authorization", (HttpRequest request) =>
        {
            var query = request.Query;
            if (query["code_challenge_method"] != "S256" || string.IsNullOrEmpty(query["code_challenge"]))
                return Results.BadRequest(new { error = "pkce_required" });
            if (!string.IsNullOrEmpty(query["code_method"]) || !string.IsNullOrEmpty(query["platform_id"]))
                return Results.BadRequest(new { error = "unexpected_parameter" });

            var code = Guid.NewGuid().ToString("N");
            MockState.Challenges[code] = query["code_challenge"].ToString();
            var redirect = query["redirect_uri"].ToString();
            var state = query["state"].ToString();
            var location = $"{redirect}{(redirect.Contains('?') ? "&" : "?")}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
            return Results.Redirect(location);
        });

        app.MapPost("/oauth/token", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var grant = form["grant_type"].ToString();
            if (grant == "refresh_token")
            {
                var refresh = form["refresh_token"].ToString();
                if (!MockState.RefreshUsers.ContainsKey(refresh))
                    return Results.BadRequest(new { error = "invalid_grant" });
                MockState.RefreshCount++;
                var next = "refresh-" + Guid.NewGuid().ToString("N");
                MockState.RefreshUsers[next] = MockState.RefreshUsers[refresh];
                return Results.Json(new
                {
                    access_token = "mock-access-" + Guid.NewGuid().ToString("N"),
                    refresh_token = next,
                    expires_in = 3600,
                    user_id = long.Parse(MockState.RefreshUsers[refresh])
                });
            }

            var code = form["code"].ToString();
            if (!MockState.Challenges.TryGetValue(code, out var expected) || MockState.UsedCodes.ContainsKey(code))
                return Results.BadRequest(new { error = "invalid_grant" });
            var verifier = form["code_verifier"].ToString();
            var actual = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
            if (actual != expected || form["client_id"].ToString() != "mock-client" || form["client_secret"].ToString() != "mock-secret")
                return Results.BadRequest(new { error = "invalid_client" });

            MockState.UsedCodes[code] = verifier;
            // Each authorization code binds a distinct Mercado Pago user so parallel e2e
            // merchants do not collide on ExternalAccountLinkedElsewhere.
            var userId = Interlocked.Increment(ref MockState.UserId);
            var refreshToken = "refresh-" + Guid.NewGuid().ToString("N");
            MockState.RefreshUsers[refreshToken] = userId.ToString();
            return Results.Json(new
            {
                access_token = "mock-access-token",
                refresh_token = refreshToken,
                expires_in = 3600,
                user_id = userId
            });
        });

        app.MapGet("/v1/payments/search", (HttpRequest request) =>
        {
            if (MockState.RateLimit == "429")
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            if (MockState.RateLimit == "2001")
                return Results.BadRequest(new { message = "2001", error = "bad_request" });
            if (request.Headers.Authorization.Count == 0)
                return Results.Unauthorized();
            MockState.SearchCount++;
            var rows = MockState.Payments.AsEnumerable();
            if (DateTimeOffset.TryParse(request.Query["begin_date"], out var from)) rows = rows.Where(row => row.GetProperty("date_created").GetDateTimeOffset() >= from);
            if (DateTimeOffset.TryParse(request.Query["end_date"], out var to)) rows = rows.Where(row => row.GetProperty("date_created").GetDateTimeOffset() <= to);
            var all = rows.ToArray();
            var offset = int.TryParse(request.Query["offset"], out var start) ? Math.Max(0, start) : 0;
            var limit = int.TryParse(request.Query["limit"], out var count) ? Math.Clamp(count, 1, 100) : 10;
            return Results.Json(new { results = all.Skip(offset).Take(limit), paging = new { total = all.Length, offset, limit } });
        });

        app.MapPost("/checkout/preferences", async (HttpRequest request) =>
        {
            MockState.LastCheckoutAuthorization = request.Headers.Authorization.ToString();
            using var reader = new StreamReader(request.Body);
            MockState.LastCheckoutBody = await reader.ReadToEndAsync();
            return Results.Json(new
            {
                id = "pref-1",
                init_point = "http://127.0.0.1:5099/checkout/pref-1"
            });
        });

        app.MapGet("/v1/payments/{id}", (string id) =>
        {
            if (id.StartsWith("unpaid", StringComparison.Ordinal))
                return Results.Json(new { id, status = "pending", external_reference = "" });
            var tenant = MockState.CheckoutTenants.TryGetValue(id, out var value) ? value : "";
            return Results.Json(new { id, status = "approved", external_reference = tenant, transaction_amount = MockState.CheckoutAmounts.GetValueOrDefault(id, 5999), currency_id = "ARS" });
        });

        app.MapPost("/google/token", () => Results.Json(new { access_token = "ya29.mock", expires_in = 3600 }));

        app.MapPost("/v1/projects/{project}/messages:send", async (HttpRequest request) =>
        {
            MockState.LastPushAuthorization = request.Headers.Authorization.ToString();
            using var reader = new StreamReader(request.Body);
            MockState.SentPushes.Add(await reader.ReadToEndAsync());
            return Results.Json(new { name = "projects/mock/messages/1" });
        });

        app.MapGet("/checkout/pref-1", () => Results.Content("<html lang='es'><meta charset='utf-8'><title>Pago de prueba</title><main><h1>Pago simulado</h1><p>Entorno local de pruebas. No se realiza ningún cobro real.</p><a href='http://localhost:5183/pagar'>Volver a TransfersCheck</a></main></html>", "text/html"));

        // Local browser tests simulate the provider's approval of the last real checkout quote.
        app.MapPost("/mock/checkout/approve", () => {
            if (string.IsNullOrWhiteSpace(MockState.LastCheckoutBody)) return Results.BadRequest();
            using var preference = JsonDocument.Parse(MockState.LastCheckoutBody);
            var paymentId = "browser-" + Guid.NewGuid().ToString("N");
            MockState.CheckoutTenants[paymentId] = preference.RootElement.GetProperty("external_reference").GetString()!;
            MockState.CheckoutAmounts[paymentId] = preference.RootElement.GetProperty("items")[0].GetProperty("unit_price").GetDecimal();
            return Results.Ok(new { paymentId, amount = MockState.CheckoutAmounts[paymentId] });
        });
        app.MapGet("/mock/pushes", () => Results.Json(MockState.SentPushes));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
