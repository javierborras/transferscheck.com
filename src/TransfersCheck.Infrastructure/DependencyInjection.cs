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
        services.AddScoped<IPlanCatalog, EfPlanCatalog>();
        services.AddScoped<IFiveDayNoticeStore, EfFiveDayNoticeStore>();
        services.AddScoped<IEmailCodeStore, EfEmailCodeStore>();
        services.AddScoped<ICommerceAliasStore, EfCommerceAliasStore>();
        services.AddScoped<IAlertInviteStore, EfAlertInviteStore>();
        services.AddScoped<ITransferHistoryStore, EfTransferHistoryStore>();
        services.AddScoped<IReferralStore, EfReferralStore>();
        services.AddSingleton<ILiveListPublisher, InMemoryLiveListPublisher>();
        services.AddScoped<IOwnerSecretStore, EfOwnerSecretStore>();
        services.AddScoped<ICheckoutAccessToken, CheckoutAccessTokenSource>();
        services.Configure<HelpCopilotOptions>(configuration.GetSection(HelpCopilotOptions.Section));
        services.AddScoped<IHelpCorpus, EfHelpCorpus>();
        services.AddHttpClient<HttpJevEvaluator>();
        services.AddScoped<IJevEvaluator>(sp => sp.GetRequiredService<HttpJevEvaluator>());
        services.AddHttpClient<HttpHelpSummarizer>();
        services.AddScoped<IHelpSummarizer>(sp => sp.GetRequiredService<HttpHelpSummarizer>());
        services.AddScoped<IHelpCopilot, HelpCopilotService>();
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.Section));
        var mailHost = configuration[$"{MailOptions.Section}:Host"] ?? "";
        var mailFrom = configuration[$"{MailOptions.Section}:FromAddress"] ?? "";
        if (!string.IsNullOrWhiteSpace(mailHost) && !string.IsNullOrWhiteSpace(mailFrom))
            services.AddSingleton<IMailSender, SmtpMailSender>();
        else
            services.AddSingleton<IMailSender, NullMailSender>();
        services.AddScoped<ITenantPlanCode, TenantPlanCode>();
        services.AddSingleton<LoginAccountCap>();
        services.AddHttpClient<MercadoPagoTransferSource>();
        services.AddScoped<ITransferSource>(sp => sp.GetRequiredService<MercadoPagoTransferSource>());
        services.AddScoped<ITransferQuerySource>(sp => sp.GetRequiredService<MercadoPagoTransferSource>());
        services.AddScoped<TransferSync>();
        services.AddHttpClient<MercadoPagoAuthorizer>();
        services.AddScoped<IMerchantAuthorizer>(sp => sp.GetRequiredService<MercadoPagoAuthorizer>());
        services.AddHttpClient<Auth0IdentityProvider>();
        services.AddScoped<IIdentityProvider>(sp => sp.GetRequiredService<Auth0IdentityProvider>());
        services.AddSingleton<IEntitlement, Entitlement>();
        services.AddHttpClient<FirebasePushSender>();
        services.AddScoped<IPushSender>(sp => sp.GetRequiredService<FirebasePushSender>());
        services.AddHttpClient<MercadoPagoCheckout>();
        services.AddScoped<ICheckout>(sp => sp.GetRequiredService<MercadoPagoCheckout>());
        services.AddSingleton<IPollLock>(_ => new SqlPollLock(configuration.GetConnectionString("TransfersCheck") ?? ""));
        return services;
    }
}
