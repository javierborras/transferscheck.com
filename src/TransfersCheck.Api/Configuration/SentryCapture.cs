using System.Reflection;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol;
using TransfersCheck.Api.Logging;

namespace TransfersCheck.Api.Configuration;

/// <summary>
/// Sends API errors when <c>Sentry:Dsn</c> is set. The value stays in host configuration.
/// <c>before_send</c> drops PII, tokens, authorization, cookies, and payment bodies (ADR-026).
/// </summary>
public static class SentryCapture
{
    public static bool Enabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Sentry:Dsn"]);

    public static void Register(WebApplicationBuilder builder)
    {
        if (!Enabled(builder.Configuration)) return;
        var dsn = builder.Configuration["Sentry:Dsn"]!;
        var release = InformationalVersion();
        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = dsn;
            options.SendDefaultPii = false;
            options.MaxRequestBodySize = RequestSize.None;
            options.EnableLogs = false;
            options.MinimumEventLevel = LogLevel.Error;
            options.MinimumBreadcrumbLevel = LogLevel.Error;
            if (release is not null) options.Release = release;
            options.SetBeforeSend(Scrub);
        });
    }

    public static SentryEvent Scrub(SentryEvent sentryEvent)
    {
        ScrubRequest(sentryEvent.Request);
        ScrubUser(sentryEvent.User);
        if (sentryEvent.Extra is IDictionary<string, object> extra) ScrubObjects(extra);
        if (sentryEvent.Tags is IDictionary<string, string> tags) RemoveSensitive(tags);
        ScrubContexts(sentryEvent.Contexts);
        foreach (var breadcrumb in sentryEvent.Breadcrumbs)
            if (breadcrumb.Data is IDictionary<string, string> data) RemoveSensitive(data);
        RedactNarrative(sentryEvent);
        return sentryEvent;
    }

    public static SentrySample Scrub(SentrySample sample)
    {
        var sentryEvent = new SentryEvent();
        foreach (var header in sample.Headers) sentryEvent.Request.Headers[header.Key] = header.Value;
        sentryEvent.Request.Cookies = sample.Cookies;
        sentryEvent.Request.Data = sample.Body;
        sentryEvent.Request.QueryString = sample.Query;
        foreach (var field in sample.Fields) sentryEvent.SetExtra(field.Key, field.Value);
        sentryEvent.User.Email = sample.Email;
        sentryEvent.User.Username = sample.Username;
        sentryEvent.User.IpAddress = sample.IpAddress;
        Scrub(sentryEvent);
        sample.Headers.Clear();
        foreach (var header in sentryEvent.Request.Headers) sample.Headers[header.Key] = header.Value;
        sample.Cookies = sentryEvent.Request.Cookies;
        sample.Body = sentryEvent.Request.Data;
        sample.Query = sentryEvent.Request.QueryString;
        sample.Fields.Clear();
        foreach (var field in sentryEvent.Extra) sample.Fields[field.Key] = field.Value;
        sample.Email = sentryEvent.User.Email;
        sample.Username = sentryEvent.User.Username;
        sample.IpAddress = sentryEvent.User.IpAddress;
        return sample;
    }

    private static string? InformationalVersion()
    {
        var version = typeof(SentryCapture).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    private static void RedactNarrative(SentryEvent sentryEvent)
    {
        if (sentryEvent.Message is { } message && LogRedaction.LooksSensitive(message.Formatted ?? message.Message))
        {
            message.Message = "Redacted.";
            message.Formatted = "Redacted.";
        }
        if (sentryEvent.SentryExceptions is null)
            return;
        foreach (var exception in sentryEvent.SentryExceptions)
            if (LogRedaction.LooksSensitive(exception.Value))
                exception.Value = "Redacted.";
    }

    private static void ScrubRequest(SentryRequest request)
    {
        request.Data = null;
        request.Cookies = null;
        request.QueryString = ScrubQuery(request.QueryString);
        RemoveSensitive(request.Headers);
        RemoveSensitive(request.Env);
        RemoveSensitive(request.Other);
        request.Env.Remove("REMOTE_ADDR");
    }

    private static void ScrubUser(SentryUser user)
    {
        user.Email = null;
        user.Username = null;
        user.IpAddress = null;
        RemoveSensitive(user.Other);
    }

    private static void ScrubContexts(SentryContexts contexts)
    {
        foreach (var key in contexts.Keys.Where(Sensitive).ToArray()) contexts.Remove(key);
        foreach (var pair in contexts.ToArray())
        {
            if (pair.Value is IDictionary<string, object> objects) ScrubObjects(objects);
            else if (pair.Value is IDictionary<string, string> strings) RemoveSensitive(strings);
            else if (pair.Value is Response response)
            {
                response.Cookies = null;
                response.Data = null;
                RemoveSensitive(response.Headers);
            }
        }
    }

    private static void ScrubObjects(IDictionary<string, object> values)
    {
        foreach (var key in values.Keys.Where(Sensitive).ToArray()) values.Remove(key);
        foreach (var value in values.Values.ToArray()) ScrubValue(value);
    }

    private static void ScrubValue(object? value)
    {
        switch (value)
        {
            case IDictionary<string, object> objects:
                ScrubObjects(objects);
                break;
            case IDictionary<string, string> strings:
                RemoveSensitive(strings);
                break;
            case string:
                break;
            case System.Collections.IEnumerable items:
                foreach (var item in items) ScrubValue(item);
                break;
        }
    }

    private static void RemoveSensitive(IDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0) return;
        foreach (var key in values.Keys.Where(Sensitive).ToArray()) values.Remove(key);
    }

    private static string? ScrubQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) return query;
        var prefix = query.StartsWith('?') ? "?" : "";
        var body = prefix.Length == 1 ? query[1..] : query;
        var kept = new List<string>();
        foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            if (!Sensitive(QueryName(part))) kept.Add(part);
        return kept.Count == 0 ? null : prefix + string.Join('&', kept);
    }

    private static string QueryName(string part)
    {
        var name = part.Split('=', 2)[0];
        try { return Uri.UnescapeDataString(name); }
        catch (UriFormatException) { return name; }
    }

    private static bool Sensitive(string key)
    {
        var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray());
        if (normalized.Length == 0) return false;
        if (normalized.Equals("pan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("cvv", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("cvc", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("card", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("ip", StringComparison.OrdinalIgnoreCase))
            return true;
        return normalized.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cardnumber", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("payer", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("email", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("username", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ipaddress", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("remoteaddr", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("forwarded", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("session", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SentrySample
{
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Cookies { get; set; }
    public object? Body { get; set; }
    public Dictionary<string, object?> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; }
    public string? Query { get; set; }
}
