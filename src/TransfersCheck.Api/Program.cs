using System.Text;
using Hangfire;
using Hangfire.Common;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TransfersCheck.Api.Accounts;
using TransfersCheck.Api.Auth;
using TransfersCheck.Api.Billing;
using TransfersCheck.Api.Configuration;
using TransfersCheck.Api.Commercial;
using TransfersCheck.Api.Connections;
using TransfersCheck.Api.Jobs;
using TransfersCheck.Api.Help;
using TransfersCheck.Api.Logging;
using TransfersCheck.Api.Owner;
using TransfersCheck.Api.Push;
using TransfersCheck.Api.Transfers;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

if (args.Length == 2 && args[0] == "--validate-production-config")
{
    var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(args[1]), optional: false).Build();
    var errors = TransfersCheck.Api.Configuration.ProductionConfiguration.Errors(configuration, "Production");
    foreach (var error in errors) Console.Error.WriteLine(error);
    if (errors.Count == 0) Console.WriteLine("Production configuration validated. No database, host, or background services started.");
    Environment.ExitCode = errors.Count == 0 ? 0 : 1;
    return;
}

var builder = WebApplication.CreateBuilder(args);
TransfersCheck.Api.Configuration.ProductionConfiguration.RequireValid(builder.Configuration, builder.Environment.EnvironmentName);
TransfersCheck.Api.Configuration.SentryCapture.Register(builder);
var errorLogs = new ErrorLogServices();
builder.Logging.AddProvider(new ErrorLogLoggerProvider(errorLogs));
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<CommercialOptions>(builder.Configuration.GetSection(CommercialOptions.Section));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.Section));
builder.Services.Configure<OnboardingOptions>(builder.Configuration.GetSection(OnboardingOptions.Section));
builder.Services.Configure<MercadoPagoOptions>(builder.Configuration.GetSection(MercadoPagoOptions.Section));
builder.Services.Configure<CheckoutOptions>(builder.Configuration.GetSection(CheckoutOptions.Section));
builder.Services.Configure<LoginSessionOptions>(builder.Configuration.GetSection(LoginSessionOptions.Section));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.Section));
builder.Services.Configure<DataProtectionPathOptions>(builder.Configuration.GetSection(DataProtectionPathOptions.Section));
builder.Services.Configure<FirebaseOptions>(builder.Configuration.GetSection(FirebaseOptions.Section));
builder.Services.Configure<UiOptions>(builder.Configuration.GetSection(UiOptions.Section));
builder.Services.Configure<Auth0Options>(builder.Configuration.GetSection(Auth0Options.Section));
builder.Services.Configure<HangfireJobsOptions>(builder.Configuration.GetSection(HangfireJobsOptions.Section));
builder.Services.Configure<OwnerOptions>(builder.Configuration.GetSection(OwnerOptions.Section));
builder.Services.Configure<HelpCopilotOptions>(builder.Configuration.GetSection(HelpCopilotOptions.Section));

var uiOrigin = builder.Configuration[$"{CorsOptions.Section}:UiOrigin"] ?? "";
builder.Services.AddCors(options =>
{
    options.AddPolicy("ui", policy =>
    {
        if (!string.IsNullOrWhiteSpace(uiOrigin))
            policy.WithOrigins(uiOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders(Correlation.HeaderName);
    });
});

builder.Services.AddTransfersCheckInfrastructure(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("TransfersCheck") ?? "";
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        PrepareSchemaIfNecessary = true
    }));
builder.Services.AddHangfireServer();

var signingKey = builder.Configuration["Session:SigningKey"] ?? "";
if (Encoding.UTF8.GetByteCount(signingKey) >= 32)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ClockSkew = TimeSpan.FromMinutes(1)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var cookieName = builder.Configuration["Session:CookieName"] ?? "tc_session";
                    if (context.Request.Cookies.TryGetValue(cookieName, out var token))
                        context.Token = token;
                    return Task.CompletedTask;
                }
            };
        });
    builder.Services.AddAuthorization();
}

builder.Services.AddSingleton<TransferPollWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TransferPollWorker>());
builder.Services.AddTransient<FiveDayNoticeJob>();
builder.Services.AddTransient<ReferralBonusJob>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "TransfersCheck", Version = "v1" });
});

var app = builder.Build();
errorLogs.Provider = app.Services;

app.MapGet("/api/status", (Microsoft.Extensions.Options.IOptions<MercadoPagoOptions> mercadoPago) => Results.Ok(new
{
    api = "online",
    mercadoPago = new { configured = !string.IsNullOrWhiteSpace(mercadoPago.Value.ApiBaseUrl) && !string.IsNullOrWhiteSpace(mercadoPago.Value.AuthorizationUrl) }
}));

// ADR-018: a tenant with a missing client id, secret or redirect would lock every commerce admin out (the local door closes). Fail at startup instead.
var auth0Options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Auth0Options>>().Value;
if (auth0Options.Enabled
    && (string.IsNullOrWhiteSpace(auth0Options.ClientId)
        || string.IsNullOrWhiteSpace(auth0Options.ClientSecret)
        || string.IsNullOrWhiteSpace(auth0Options.RedirectUri)))
    throw new InvalidOperationException("Auth0:Authority is set, so Auth0:ClientId, Auth0:ClientSecret and Auth0:RedirectUri are required.");

// The repo has no migrations and EnsureCreated skips any database that already has tables, so an older database drifts from the
// model (missing LoginAccounts.PasswordSet, whole tables). SchemaBootstrap adds what is missing and never alters or drops.
// Development and Test always run it; other environments opt in with Database:EnsureSchema=true.
if (!string.IsNullOrWhiteSpace(connectionString)
    && (app.Environment.IsEnvironment("Test") || app.Environment.IsDevelopment()
        || app.Configuration.GetValue<bool>("Database:EnsureSchema")))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
    await SchemaBootstrap.EnsureAsync(db, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SchemaBootstrap"));
}

// Product schedules from HangfireJobs (defaults: daily noon UTC / every six hours). ADR-011 names the jobs; cron is typed config.
var hangfireJobs = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HangfireJobsOptions>>().Value;
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<FiveDayNoticeJob>("five-day-notice", job => job.Execute(), hangfireJobs.FiveDayNoticeCron);
recurringJobs.AddOrUpdate<ReferralBonusJob>("referral-bonus", job => job.Execute(), hangfireJobs.ReferralBonusCron);

app.UseCors("ui");
Correlation.Use(app);
if (Encoding.UTF8.GetByteCount(signingKey) >= 32)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TransfersCheck");
});

if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new AuthenticatedDashboardFilter()]
    });
}

AccountEndpoints.Map(app);
Auth0Endpoints.Map(app);
#if DEBUG
DevPlanEndpoints.Map(app);
#endif
OwnerEndpoints.Map(app);
HelpEndpoints.Map(app);
ConnectionEndpoints.Map(app);
BillingEndpoints.Map(app);
TransferEndpoints.Map(app);
HistoryEndpoints.Map(app);
PushEndpoints.Map(app);
CommercialEndpoints.Map(app);

app.Run();

public partial class Program;
