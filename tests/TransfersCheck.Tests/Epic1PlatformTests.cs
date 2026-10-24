using System.Reflection;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TransfersCheck.Api.Transfers;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Tests;

[Collection("sql")]
public sealed class Epic1PlatformTests
{
    private readonly SqlFixture _sql;

    public Epic1PlatformTests(SqlFixture sql) => _sql = sql;

    [Fact]
    public void Core_does_not_reference_ef_core_or_aspnet()
    {
        var names = typeof(Merchant).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain(names, name => name != null && name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name != null && name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Backend_is_three_projects_and_the_image_excludes_the_pwa_and_sql()
    {
        var root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "src", "TransfersCheck.Core", "TransfersCheck.Core.csproj")));
        Assert.True(File.Exists(Path.Combine(root, "src", "TransfersCheck.Infrastructure", "TransfersCheck.Infrastructure.csproj")));
        Assert.True(File.Exists(Path.Combine(root, "src", "TransfersCheck.Api", "TransfersCheck.Api.csproj")));
        var webPackage = File.ReadAllText(Path.Combine(root, "web", "package.json"));
        Assert.Contains("vite", webPackage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("react", webPackage, StringComparison.OrdinalIgnoreCase);
        var docker = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        Assert.DoesNotContain("web/", docker, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mssql", docker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TransfersCheck.Api.dll", docker, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typed_configuration_carries_the_locked_commercial_numbers()
    {
        await using var factory = new TransfersApiFactory(_sql);
        using var scope = factory.Services.CreateScope();
        var commercial = scope.ServiceProvider.GetRequiredService<IOptions<CommercialOptions>>().Value;
        var sync = scope.ServiceProvider.GetRequiredService<IOptions<SyncOptions>>().Value;
        var onboarding = scope.ServiceProvider.GetRequiredService<IOptions<OnboardingOptions>>().Value;
        var session = scope.ServiceProvider.GetRequiredService<IOptions<LoginSessionOptions>>().Value;
        Assert.Equal(500, commercial.WeeklyQueryCap);
        Assert.Equal(50, commercial.WeeklyQueryWarningGap);
        Assert.Equal("ARS", commercial.Currency);
        Assert.Equal(30, commercial.PaidPeriodDays);
        Assert.Equal(10, sync.PageSize);
        Assert.Equal(10, sync.AlertPointRecordCount);
        Assert.Equal(TimeSpan.FromMinutes(5), sync.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), sync.ButtonCooldown);
        Assert.Equal("", onboarding.MercadoPagoVideoUrl);
        Assert.Equal(TimeSpan.FromHours(1), session.EmailCodeLifetime);
        Assert.Equal(TimeSpan.FromHours(1), session.InviteLinkLifetime);
        Assert.Null(typeof(CommercialOptions).GetProperty("TrialDays", BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(typeof(CommercialOptions).GetProperty("PriceAmount", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task Composition_maps_only_mercadopago_and_the_four_ports()
    {
        await using var factory = new TransfersApiFactory(_sql);
        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<ITransferSource>();
        var sources = scope.ServiceProvider.GetServices<ITransferSource>().ToArray();
        Assert.Equal("mercadopago", source.SourceCode);
        Assert.Single(sources);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMerchantAuthorizer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEntitlement>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMerchantStore>());
        var level = scope.ServiceProvider.GetRequiredService<DbContextOptions<TransfersCheckDb>>()
            .Extensions.Single(extension => extension.GetType().Name == "SqlServerOptionsExtension");
        var compatibility = level.GetType().GetProperty("SqlServerCompatibilityLevel")?.GetValue(level);
        Assert.Equal(SqlCompat.Level, Convert.ToInt32(compatibility));
        Assert.Equal(150, SqlCompat.Level);
    }

    [Fact]
    public async Task A_read_for_one_merchant_returns_only_that_merchants_rows()
    {
        var options = new DbContextOptionsBuilder<TransfersCheckDb>()
            .UseSqlServer(_sql.ConnectionString, sql => sql.UseCompatibilityLevel(SqlCompat.Level))
            .Options;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var contextA = new TenantContext { TenantId = tenantA };
        await using (var db = new TransfersCheckDb(options, contextA))
        {
            db.Transfers.Add(TransferFor(tenantA, "a-1"));
            await db.SaveChangesAsync();
        }

        var contextB = new TenantContext { TenantId = tenantB };
        await using (var db = new TransfersCheckDb(options, contextB))
        {
            db.Transfers.Add(TransferFor(tenantB, "b-1"));
            await db.SaveChangesAsync();
        }

        await using (var db = new TransfersCheckDb(options, contextA))
        {
            var rows = await db.Transfers.ToListAsync();
            Assert.Single(rows);
            Assert.Equal(tenantA, rows[0].TenantId);
            Assert.Equal("a-1", rows[0].ExternalId);
        }

        await using var probe = new TransfersCheckDb(options, contextA);
        await using var connection = probe.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()";
        var level = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Equal(150, level);
    }

    [Fact]
    public async Task Swagger_serves_the_contract_and_cors_allow_lists_the_ui_origin()
    {
        await using var factory = new TransfersApiFactory(_sql);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        request.Headers.Add("Origin", "http://localhost:5183");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"openapi\"", json, StringComparison.Ordinal);
        Assert.Equal("http://localhost:5183", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Hangfire_registers_only_the_five_day_notice_and_referral_bonus_jobs()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Api", "TransfersCheck.Api.csproj"));
        Assert.Contains("""PackageReference Include="Hangfire.AspNetCore" Version="1.8.25" """, csproj, StringComparison.Ordinal);
        Assert.Contains("""PackageReference Include="Hangfire.SqlServer" Version="1.8.25" """, csproj, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TransfersCheck.Api", "Program.cs"));
        Assert.Contains("MapHangfireDashboard(\"/hangfire\"", program, StringComparison.Ordinal);
        Assert.Contains("IsDevelopment()", program, StringComparison.Ordinal);

        await using var factory = new TransfersApiFactory(_sql);
        var storage = factory.Services.GetRequiredService<JobStorage>();
        var recurring = storage.GetConnection().GetRecurringJobs();
        Assert.Equal(2, recurring.Count);
        Assert.Contains(recurring, job => job.Id == "five-day-notice");
        Assert.Contains(recurring, job => job.Id == "referral-bonus");
        Assert.DoesNotContain(recurring, job =>
            job.Id.Contains("poll", StringComparison.OrdinalIgnoreCase) ||
            (job.Job?.ToString().Contains("TransferPollWorker", StringComparison.Ordinal) ?? false));

        Assert.Contains(factory.Services.GetServices<IHostedService>(), service => service is TransferPollWorker);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        using var dashboard = await client.GetAsync("/hangfire");
        Assert.True(dashboard.IsSuccessStatusCode, $"Expected /hangfire in Development, got {(int)dashboard.StatusCode}");
    }

    private static Transfer TransferFor(Guid tenantId, string externalId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Source = "mercadopago",
        ExternalId = externalId,
        PayerName = "Alguien",
        Amount = 100m,
        Currency = "ARS",
        OccurredAt = DateTimeOffset.UtcNow,
        Status = "approved",
        TypeCode = "bank_transfer"
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransfersCheck.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}

public sealed class SqlFixture : IAsyncLifetime
{
    public string ConnectionString { get; } =
        "Server=localhost\\SQLEXPRESS;Database=TransfersCheckTests;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    private IAsyncDisposable? _mock;

    public async Task InitializeAsync()
    {
        TransfersCheck.MockServer.MockState.Reset();
        _mock = await TransfersCheck.MockServer.Program.ListenAsync("http://127.0.0.1:5099");

        var options = new DbContextOptionsBuilder<TransfersCheckDb>()
            .UseSqlServer(ConnectionString, sql => sql.UseCompatibilityLevel(SqlCompat.Level))
            .Options;
        await using var db = new TransfersCheckDb(options, new TenantContext());
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_mock is not null)
            await _mock.DisposeAsync();
    }
}

[CollectionDefinition("sql", DisableParallelization = true)]
public sealed class SqlCollection : ICollectionFixture<SqlFixture>;

public sealed class MutableClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class TransfersApiFactory : WebApplicationFactory<Program>
{
    private readonly SqlFixture _sql;
    public TimeProvider? Clock { get; init; }
    public string? VideoUrl { get; init; }
    public int? PageSize { get; init; }
    public string? ServiceAccountPath { get; init; }
    public RecordingMailSender? Mail { get; init; }

    public TransfersApiFactory(SqlFixture sql) => _sql = sql;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:TransfersCheck", _sql.ConnectionString);
        builder.UseSetting("Session:SigningKey", "test-signing-key-with-32-bytes-min");
        builder.UseSetting("DataProtection:KeyPath", Path.Combine(Path.GetTempPath(), "transferscheck-test-keys"));
        builder.UseSetting("Cors:UiOrigin", "http://localhost:5183");
        builder.UseSetting("Ui:Origin", "http://localhost:5183");
        builder.UseSetting("MercadoPago:ClientId", "mock-client");
        builder.UseSetting("MercadoPago:ClientSecret", "mock-secret");
        builder.UseSetting("MercadoPago:AuthorizationUrl", "http://127.0.0.1:5099/authorization");
        builder.UseSetting("MercadoPago:TokenUrl", "http://127.0.0.1:5099/oauth/token");
        builder.UseSetting("MercadoPago:ApiBaseUrl", "http://127.0.0.1:5099");
        builder.UseSetting("MercadoPago:RedirectUri", "http://127.0.0.1:5099/ignored-callback");
        builder.UseSetting("Checkout:AccessToken", "mock-checkout-token");
        builder.UseSetting("Checkout:WebhookSecret", "test-webhook-secret");
        builder.UseSetting("Checkout:NotificationUrl", "http://127.0.0.1:5080/api/billing/webhook");
        builder.UseSetting("Checkout:ReturnUrl", "http://localhost:5183/");
        builder.UseSetting("Firebase:MessagingUrl", "http://127.0.0.1:5099/v1/projects/mock/messages:send");
        builder.UseSetting("Firebase:ProjectId", "mock");
        builder.UseSetting("Firebase:Scope", "https://www.googleapis.com/auth/firebase.messaging");
        if (ServiceAccountPath is not null)
            builder.UseSetting("Firebase:ServiceAccountPath", ServiceAccountPath);
        if (VideoUrl is not null)
            builder.UseSetting("Onboarding:MercadoPagoVideoUrl", VideoUrl);
        if (PageSize is not null)
            builder.UseSetting("Sync:PageSize", PageSize.Value.ToString());
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            if (Clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(Clock);
            }
            if (Mail is not null)
            {
                services.RemoveAll<IMailSender>();
                services.AddSingleton<IMailSender>(Mail);
            }
        });
    }
}
