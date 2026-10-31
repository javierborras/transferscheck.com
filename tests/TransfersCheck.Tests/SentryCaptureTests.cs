using Microsoft.Extensions.Configuration;
using TransfersCheck.Api.Configuration;

namespace TransfersCheck.Tests;

public sealed class SentryCaptureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_dsn_leaves_sentry_disabled(string? dsn)
    {
        var values = new Dictionary<string, string?>();
        if (dsn is not null) values["Sentry:Dsn"] = dsn;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.False(SentryCapture.Enabled(config));
    }

    [Fact]
    public void Sample_event_drops_authorization_cookie_and_payment_fields()
    {
        var sample = new SentrySample
        {
            Cookies = "tc_session=session-value",
            Body = new Dictionary<string, object?>
            {
                ["card_number"] = "4111111111111111",
                ["token"] = "pay-token",
                ["amount"] = 10
            },
            Email = "person@example.com",
            Username = "person",
            IpAddress = "203.0.113.8",
            Query = "page=1&access_token=secret-token",
            Headers =
            {
                ["Authorization"] = "Bearer secret-token",
                ["Cookie"] = "tc_session=session-value",
                ["Accept"] = "application/json"
            },
            Fields =
            {
                ["access_token"] = "secret-token",
                ["payment"] = new Dictionary<string, object?> { ["cvv"] = "123" },
                ["route"] = "/api/billing/webhook"
            }
        };

        SentryCapture.Scrub(sample);

        Assert.False(sample.Headers.ContainsKey("Authorization"));
        Assert.False(sample.Headers.ContainsKey("Cookie"));
        Assert.Equal("application/json", sample.Headers["Accept"]);
        Assert.Null(sample.Cookies);
        Assert.Null(sample.Body);
        Assert.False(sample.Fields.ContainsKey("access_token"));
        Assert.False(sample.Fields.ContainsKey("payment"));
        Assert.Equal("/api/billing/webhook", sample.Fields["route"]);
        Assert.Null(sample.Email);
        Assert.Null(sample.Username);
        Assert.Null(sample.IpAddress);
        Assert.DoesNotContain("access_token", sample.Query ?? "", StringComparison.Ordinal);
        Assert.Contains("page=1", sample.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", sample.Query ?? "", StringComparison.Ordinal);
    }
}
