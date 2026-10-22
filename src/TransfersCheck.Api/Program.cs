using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TransfersCheck.Api.Accounts;
using TransfersCheck.Api.Connections;
using TransfersCheck.Api.Transfers;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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

var uiOrigin = builder.Configuration[$"{CorsOptions.Section}:UiOrigin"] ?? "";
builder.Services.AddCors(options =>
{
    options.AddPolicy("ui", policy =>
    {
        if (!string.IsNullOrWhiteSpace(uiOrigin))
            policy.WithOrigins(uiOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

builder.Services.AddTransfersCheckInfrastructure(builder.Configuration);

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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "TransfersCheck", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsEnvironment("Test") || app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TransfersCheckDb>();
    var connection = builder.Configuration.GetConnectionString("TransfersCheck");
    if (!string.IsNullOrWhiteSpace(connection))
        db.Database.EnsureCreated();
}

app.UseCors("ui");
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

AccountEndpoints.Map(app);
ConnectionEndpoints.Map(app);
TransferEndpoints.Map(app);

app.Run();

public partial class Program;
