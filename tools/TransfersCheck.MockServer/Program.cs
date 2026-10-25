namespace TransfersCheck.MockServer;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var app = Build(args);
        app.Run();
    }

    public static WebApplication Build(string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);
        RequireDevelopment(builder.Environment.EnvironmentName);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        MockEndpoints.Map(app);
        return app;
    }

    public static async Task<IAsyncDisposable> ListenAsync(string url)
    {
        RequireDevelopment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Test");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url);
        var app = builder.Build();
        MockEndpoints.Map(app);
        await app.StartAsync();
        return app;
    }

    private static void RequireDevelopment(string environment)
    {
        if (environment is not ("Development" or "Test"))
            throw new InvalidOperationException("The mock server is restricted to Development/Test and must never run in production.");
    }
}
