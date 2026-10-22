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
        builder.Logging.ClearProviders();
        var app = builder.Build();
        MockEndpoints.Map(app);
        return app;
    }

    public static async Task<IAsyncDisposable> ListenAsync(string url)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url);
        var app = builder.Build();
        MockEndpoints.Map(app);
        await app.StartAsync();
        return app;
    }
}
