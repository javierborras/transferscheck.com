using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTransfersCheckInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var keyPath = configuration[$"{DataProtectionPathOptions.Section}:KeyPath"];
        var dataProtection = services.AddDataProtection().SetApplicationName("TransfersCheck");
        if (!string.IsNullOrWhiteSpace(keyPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<ITokenProtector, DataProtectionTokenProtector>();
        services.AddDbContext<TransfersCheckDb>(options =>
        {
            var connectionString = configuration.GetConnectionString("TransfersCheck");
            options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(SqlCompat.Level));
        });
        services.AddScoped<IMerchantStore, EfMerchantStore>();
        services.AddScoped<ITransferSource, MercadoPagoTransferSource>();
        services.AddHttpClient<MercadoPagoAuthorizer>();
        services.AddScoped<IMerchantAuthorizer>(sp => sp.GetRequiredService<MercadoPagoAuthorizer>());
        services.AddSingleton<IEntitlement, Entitlement>();
        services.AddSingleton<IPushSender, UnavailablePushSender>();
        services.AddSingleton<ICheckout, UnavailableCheckout>();
        services.AddSingleton<IPollLock>(_ => new SqlPollLock(configuration.GetConnectionString("TransfersCheck") ?? ""));
        return services;
    }
}
