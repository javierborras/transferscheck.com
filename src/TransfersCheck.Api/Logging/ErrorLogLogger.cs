using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using TransfersCheck.Api.Configuration;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Api.Logging;

/// <summary>Holds the built host so error rows can be written without a logging/DI cycle.</summary>
public sealed class ErrorLogServices
{
    public IServiceProvider? Provider { get; set; }
}

public sealed class ErrorLogLoggerProvider(ErrorLogServices services) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new ErrorLogLogger(categoryName, services, () => _scopes);

    public void Dispose() { }

    private sealed class ErrorLogLogger(string category, ErrorLogServices services, Func<IExternalScopeProvider> scopes) : ILogger
    {
        private static readonly AsyncLocal<bool> Busy = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || Busy.Value || services.Provider is null || Skip(category))
                return;

            var message = LogRedaction.SafeMessage(state, formatter);
            if (message.Length == 0)
                return;

            Busy.Value = true;
            try
            {
                using var scope = services.Provider.CreateScope();
                var http = scope.ServiceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
                var row = new ErrorLog
                {
                    OccurredAt = (scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow(),
                    CorrelationId = Trim(ReadCorrelation(scopes(), http), 64),
                    Message = message,
                    Path = Trim(http?.Request.Path.Value ?? "", 256),
                    Level = logLevel == LogLevel.Critical ? "Critical" : "Error"
                };
                var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
                db.ErrorLogs.Add(row);
                db.SaveChanges();
            }
            catch
            {
                // A failed audit row must not replace the original error.
            }
            finally
            {
                Busy.Value = false;
            }
        }

        private static bool Skip(string name)
            => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal)
            || name.StartsWith("System.Net.Http", StringComparison.Ordinal)
            || name.StartsWith("Hangfire", StringComparison.Ordinal);

        private static string ReadCorrelation(IExternalScopeProvider provider, HttpContext? http)
        {
            var found = "";
            provider.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> nullable)
                {
                    foreach (var pair in nullable)
                        if (pair.Key == Correlation.ItemKey && pair.Value is string text && text.Length > 0)
                            found = text;
                }
                else if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var pair in pairs)
                        if (pair.Key == Correlation.ItemKey && pair.Value is string text && text.Length > 0)
                            found = text;
                }
            }, 0);
            if (found.Length > 0)
                return found;
            return http?.Items[Correlation.ItemKey] as string ?? "";
        }

        private static string Trim(string value, int max)
            => value.Length <= max ? value : value[..max];
    }
}

public static partial class LogRedaction
{
    public static string SafeMessage<TState>(TState state, Func<TState, Exception?, string> formatter)
    {
        var rendered = Render(state, formatter);
        rendered = Assignment().Replace(rendered, "[redacted]");
        rendered = Pan().Replace(rendered, "[redacted]");
        rendered = rendered.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return rendered.Length <= 400 ? rendered : rendered[..400];
    }

    public static bool LooksSensitive(string? text)
        => !string.IsNullOrEmpty(text) && (Assignment().IsMatch(text) || Pan().IsMatch(text));

    private static string Render<TState>(TState state, Func<TState, Exception?, string> formatter)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
            return formatter(state, null);

        string? template = null;
        foreach (var pair in pairs)
            if (pair.Key == "{OriginalFormat}")
                template = pair.Value?.ToString();
        if (template is null)
            return formatter(state, null);

        foreach (var pair in pairs)
        {
            if (pair.Key.Length == 0 || pair.Key[0] == '{')
                continue;
            var value = SensitiveKey(pair.Key) ? "[redacted]" : pair.Value?.ToString() ?? "";
            template = template.Replace("{" + pair.Key + "}", value, StringComparison.Ordinal);
        }
        return template;
    }

    private static bool SensitiveKey(string key)
    {
        var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray());
        if (normalized.Length == 0)
            return false;
        return normalized.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("payer", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("card", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cvv", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cvc", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("pan", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("session", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("""(?i)\b(payment|payer|card_number|cardnumber|cvv|cvc|pan|token|secret|password|authorization|cookie|credential|session)\b\s*[:=]\s*(\[redacted\]|"[^"]*"|'[^']*'|[^\s,}&]+)""")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\b\d{13,19}\b")]
    private static partial Regex Pan();
}
