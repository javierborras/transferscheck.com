using System.Security.Cryptography;
using System.Text;
using TransfersCheck.Api.Billing;
using TransfersCheck.Core;

namespace TransfersCheck.Tests;

public sealed class WebhookSignatureTests
{
    private const string Key = "notification-key";
    private static readonly string SessionKey = new('s', 48);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");

    private static string Sign(string id, string requestId, long ts, string key = Key)
    {
        var manifest = $"id:{id.ToLowerInvariant()};request-id:{requestId};ts:{ts};";
        return $"ts={ts},v1={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant()}";
    }

    private static CheckoutOptions Official() => new() { NotificationSigningKey = Key, WebhookSecret = "shared" };

    [Fact]
    public void Official_signature_is_accepted()
        => Assert.True(WebhookAuthenticator.IsAuthentic(Official(), null, Sign("ABC123", "req-1", Now.ToUnixTimeMilliseconds()), "req-1", "ABC123", Now));

    [Fact]
    public void Wrong_key_tampered_id_or_request_id_are_refused()
    {
        var ts = Now.ToUnixTimeMilliseconds();
        Assert.False(WebhookAuthenticator.IsAuthentic(Official(), null, Sign("1", "r", ts, "other-key"), "r", "1", Now));
        Assert.False(WebhookAuthenticator.IsAuthentic(Official(), null, Sign("1", "r", ts), "r", "2", Now));
        Assert.False(WebhookAuthenticator.IsAuthentic(Official(), null, Sign("1", "r", ts), "other", "1", Now));
    }

    [Fact]
    public void Stale_timestamp_is_refused()
        => Assert.False(WebhookAuthenticator.IsAuthentic(Official(), null, Sign("1", "r", Now.AddHours(-1).ToUnixTimeMilliseconds()), "r", "1", Now));

    [Fact]
    public void Shared_header_is_refused_once_a_signing_key_exists()
        => Assert.False(WebhookAuthenticator.IsAuthentic(Official(), "shared", null, null, "1", Now));

    [Fact]
    public void Signed_notification_url_reference_is_accepted_and_tampered_reference_is_refused()
    {
        var url = CheckoutNotificationSignature.BuildNotificationUrl("https://auth.transferscheck.com/api/billing/webhook", "order:abc123", SessionKey);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        var opts = new CheckoutOptions();
        Assert.True(WebhookAuthenticator.IsAuthentic(opts, null, null, null, "99", Now, SessionKey, query["ref"], query["sig"]));
        Assert.False(WebhookAuthenticator.IsAuthentic(opts, null, null, null, "99", Now, SessionKey, "order:other", query["sig"]));
        Assert.False(WebhookAuthenticator.IsAuthentic(opts, null, null, null, "99", Now, new string('w', 48), query["ref"], query["sig"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("ts=abc,v1=zz")]
    public void Malformed_signature_is_refused(string signature)
        => Assert.False(WebhookAuthenticator.IsAuthentic(Official(), null, signature, "r", "1", Now));

    [Fact]
    public void Development_shared_header_still_works_without_a_signing_key()
    {
        var options = new CheckoutOptions { WebhookSecret = "shared" };
        Assert.True(WebhookAuthenticator.IsAuthentic(options, "shared", null, null, "1", Now));
        Assert.False(WebhookAuthenticator.IsAuthentic(options, "wrong", null, null, "1", Now));
        Assert.False(WebhookAuthenticator.IsAuthentic(new CheckoutOptions(), "", null, null, "1", Now));
    }
}
