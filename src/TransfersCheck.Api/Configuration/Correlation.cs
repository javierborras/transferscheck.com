using Sentry;

namespace TransfersCheck.Api.Configuration;

public static class Correlation
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public static void Use(WebApplication app)
    {
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            var incoming = context.Request.Headers[HeaderName].ToString().Trim();
            var correlationId = Accept(incoming) ? incoming : Guid.NewGuid().ToString("N");
            context.Items[ItemKey] = correlationId;
            context.Response.Headers[HeaderName] = correlationId;
            if (SentrySdk.IsEnabled)
                SentrySdk.ConfigureScope(scope => scope.SetTag("correlation_id", correlationId));

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TransfersCheck.Correlation");
            using (logger.BeginScope("CorrelationId={CorrelationId}", correlationId))
                await next(context);
        });
    }

    public static bool Accept(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            return false;
        foreach (var character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
                return false;
        return true;
    }
}
