using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;

namespace TransfersCheck.Infrastructure;

public sealed class TransfersCheckDb : DbContext
{
    private readonly ITenantContext _tenant;

    public TransfersCheckDb(DbContextOptions<TransfersCheckDb> options, ITenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<LoginAccount> LoginAccounts => Set<LoginAccount>();
    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<OAuthAttempt> OAuthAttempts => Set<OAuthAttempt>();
    public DbSet<MerchantConnection> Connections => Set<MerchantConnection>();
    public DbSet<TransferType> TransferTypes => Set<TransferType>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<ProcessedPayment> ProcessedPayments => Set<ProcessedPayment>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<EmailSignInChallenge> EmailSignInChallenges => Set<EmailSignInChallenge>();
    public DbSet<AlertInvite> AlertInvites => Set<AlertInvite>();
    public DbSet<ReferralGrant> ReferralGrants => Set<ReferralGrant>();
    public DbSet<BillingOrder> BillingOrders => Set<BillingOrder>();
    public DbSet<OwnerSecretRow> OwnerSecrets => Set<OwnerSecretRow>();
    public DbSet<HelpArticle> HelpArticles => Set<HelpArticle>();
    public DbSet<HelpCorpusState> HelpCorpusStates => Set<HelpCorpusState>();
    public DbSet<IncidentLatch> IncidentLatches => Set<IncidentLatch>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    public DbSet<CommerceHelpMessage> CommerceHelpMessages => Set<CommerceHelpMessage>();
    public DbSet<MercadoPagoApiCall> MercadoPagoApiCalls => Set<MercadoPagoApiCall>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HelpArticle>(entity =>
        {
            entity.HasKey(a => new { a.Id, a.CorpusVersion });
            entity.Property(a => a.Id).HasMaxLength(64);
            entity.Property(a => a.Title).HasMaxLength(128);
            entity.Property(a => a.Summary).HasMaxLength(512);
            entity.Property(a => a.Body).HasMaxLength(8000);
            entity.Property(a => a.PublicPath).HasMaxLength(128);
            entity.Property(a => a.Keywords).HasMaxLength(256);
            entity.HasData(
                SeedArticle("conectar", "Conectar Mercado Pago",
                    "Vinculá la cuenta de cobro desde el checklist de Configurar. El video de onboarding no es el corpus de ayuda.",
                    "Abrí Configurar, tocá Conectar Mercado Pago y autorizá la cuenta. Tras el link arranca el plan o la prueba. La ayuda vive en estas páginas; el video solo acompaña el primer recorrido.",
                    "conectar oauth mercadopago checklist", 1),
                SeedArticle("punto-alerta", "Punto de Alerta",
                    "El Punto de Alerta ve las últimas filas y puede abrir el detalle. No administra cuentas ni el historial completo.",
                    "El admin crea un nombre y comparte un link de una hora. Esa persona entra con usuario y clave, ve las últimas transferencias y puede abrir el detalle de una fila cuando Mercado Pago no muestra el pagador.",
                    "punto alerta invite usuario clave", 1),
                SeedArticle("pagar", "Pagar el Pack Mes",
                    "En Pagos elegís el Pack Mes y seguís el link de Mercado Pago hacia la cuenta del dueño del producto.",
                    "El Pack Mes dura 30 días y habilita hasta cinco cuentas. El Pack Evento dura 24 horas. Los precios los edita el dueño en el microsite; la pantalla de Pagos del comercio solo muestra los planes activos.",
                    "pagar pack mes evento precio", 1),
                SeedArticle("transferencias", "Transferencias en vivo",
                    "La actualización automática se espacia según actividad; el botón espera diez segundos entre consultas.",
                    "En mostrador ves Fecha, Pagador, Monto y Estado. Un pagador vacío se muestra como Desconocido. No hay cupos comerciales de consultas. Los controles de espera son técnicos.",
                    "transferencias lista refresco consulta cuota", 1));
        });

        modelBuilder.Entity<HelpCorpusState>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasData(new HelpCorpusState { Id = 1, PublishedVersion = 1, PublishedAt = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero) });
        });

        modelBuilder.Entity<OwnerSecretRow>(entity =>
        {
            entity.HasKey(s => s.Card);
            entity.Property(s => s.Card).HasMaxLength(64);
            entity.Property(s => s.ValueProtected).HasMaxLength(4000);
            entity.Property(s => s.LastFour).HasMaxLength(4);
        });

        modelBuilder.Entity<ReferralGrant>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Outcome).HasMaxLength(32);
        });

        modelBuilder.Entity<BillingOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            entity.Property(e => e.PlanCode).HasMaxLength(32);
            entity.Property(e => e.Currency).HasMaxLength(8);
            entity.HasQueryFilter(e => _tenant.TenantId != Guid.Empty && e.TenantId == _tenant.TenantId);
        });


        modelBuilder.Entity<EmailSignInChallenge>(entity =>
        {
            entity.HasKey(c => c.AccountId);
            entity.Property(c => c.Email).HasMaxLength(256);
            entity.Property(c => c.CodeHash).HasMaxLength(64);
            entity.HasIndex(c => c.Email);
        });

        modelBuilder.Entity<AlertInvite>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Token).HasMaxLength(64);
            entity.HasIndex(i => i.Token).IsUnique();
            entity.HasQueryFilter(i => _tenant.TenantId != Guid.Empty && i.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<Plan>(entity =>
        {
            entity.HasKey(p => p.Code);
            entity.Property(p => p.Code).HasMaxLength(32);
            entity.Property(p => p.DisplayName).HasMaxLength(64);
            entity.Property(p => p.Currency).HasMaxLength(8);
            entity.Property(p => p.FaceCopy).HasMaxLength(256);
            entity.HasData(
                new Plan
                {
                    Code = PlanCodes.PackEvento,
                    DisplayName = "Pack Evento",
                    Amount = 1000,
                    Currency = "ARS",
                    DurationHours = 24,
                    FaceCopy = "Un día, un usuario. Ideal para la fiesta o el show.",
                    EnableCopilotHelp = false,
                    Active = true
                },
                new Plan
                {
                    Code = PlanCodes.PackMes,
                    DisplayName = "Pack Mes",
                    Amount = 5999,
                    Currency = "ARS",
                    DurationDays = 30,
                    FaceCopy = "",
                    EnableCopilotHelp = true,
                    Active = true
                },
                new Plan
                {
                    Code = PlanCodes.PackCorporativo,
                    DisplayName = "Pack Corporativo",
                    Amount = 15999,
                    Currency = "ARS",
                    DurationDays = 30,
                    FaceCopy = "Escala y multi-sucursal para equipos que necesitan crecer.",
                    EnableCopilotHelp = true,
                    Active = true
                });
        });

        modelBuilder.Entity<Merchant>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Ignore(m => m.TenantId);
            entity.Property(m => m.ChecklistStep).HasMaxLength(32);
            entity.Property(m => m.PlanCode).HasMaxLength(32);
            entity.Property(m => m.Alias).HasMaxLength(32);
            entity.Property(m => m.ReferralCode).HasMaxLength(16);
            entity.Property(m => m.PendingReferralCode).HasMaxLength(16);
            entity.HasIndex(m => m.Alias).IsUnique().HasFilter("[Alias] IS NOT NULL");
            entity.HasIndex(m => m.ReferralCode).IsUnique().HasFilter("[ReferralCode] IS NOT NULL");
            entity.HasQueryFilter(m => _tenant.TenantId != Guid.Empty && m.Id == _tenant.TenantId);
        });

        modelBuilder.Entity<LoginAccount>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.AlertPointPosition).HasDefaultValue(0);
            entity.Property(a => a.RenewWithAdmin).HasDefaultValue(true);
            entity.Property(a => a.Email).HasMaxLength(256);
            entity.Property(a => a.Auth0Subject).HasMaxLength(128);
            entity.Property(a => a.UserName).HasMaxLength(32);
            entity.Property(a => a.PasswordHash).HasMaxLength(500);
            entity.HasIndex(a => a.Email).IsUnique();
            entity.HasIndex(a => a.Auth0Subject).IsUnique().HasFilter("[Auth0Subject] IS NOT NULL");
            entity.HasQueryFilter(a => _tenant.TenantId != Guid.Empty && a.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<LoginSession>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasQueryFilter(s => _tenant.TenantId != Guid.Empty && s.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<OAuthAttempt>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.State).HasMaxLength(128);
            entity.HasIndex(a => a.State).IsUnique();
            entity.HasQueryFilter(a => _tenant.TenantId != Guid.Empty && a.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<MerchantConnection>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Source).HasMaxLength(32);
            entity.Property(c => c.ExternalAccountId).HasMaxLength(64);
            entity.HasIndex(c => c.ExternalAccountId).IsUnique();
            entity.HasQueryFilter(c => _tenant.TenantId != Guid.Empty && c.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<TransferType>(entity =>
        {
            entity.HasKey(t => t.Code);
            entity.Property(t => t.Code).HasMaxLength(64);
            entity.HasData(
                new TransferType { Code = "bank_transfer" },
                new TransferType { Code = "account_money" });
        });

        modelBuilder.Entity<Transfer>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Source).HasMaxLength(32);
            entity.Property(t => t.ExternalId).HasMaxLength(64);
            entity.Property(t => t.PayerName).HasMaxLength(256);
            entity.Property(t => t.Currency).HasMaxLength(8);
            entity.Property(t => t.Status).HasMaxLength(32);
            entity.Property(t => t.StatusDetail).HasMaxLength(64);
            entity.Property(t => t.TypeCode).HasMaxLength(64);
            entity.Property(t => t.OperationType).HasMaxLength(64);
            entity.Property(t => t.PaymentMethodId).HasMaxLength(64);
            entity.Property(t => t.CoelsaId).HasMaxLength(128);
            entity.Property(t => t.BankTransferId).HasMaxLength(64);
            entity.Property(t => t.PayerDocument).HasMaxLength(64);
            entity.Property(t => t.PayerEmail).HasMaxLength(256);
            entity.Property(t => t.OriginBank).HasMaxLength(128);
            entity.Property(t => t.OriginAccount).HasMaxLength(64);
            entity.Property(t => t.Description).HasMaxLength(256);
            entity.Property(t => t.Amount).HasPrecision(18, 2);
            entity.HasIndex(t => new { t.TenantId, t.Source, t.ExternalId }).IsUnique();
            entity.HasQueryFilter(t => _tenant.TenantId != Guid.Empty && t.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<PushDevice>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Token).HasMaxLength(512);
            entity.HasIndex(d => d.Token).IsUnique();
            entity.HasQueryFilter(d => _tenant.TenantId != Guid.Empty && d.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<ProcessedPayment>(entity =>
        {
            entity.HasKey(p => p.PaymentId);
            entity.Property(p => p.PaymentId).HasMaxLength(64);
            entity.HasQueryFilter(p => _tenant.TenantId != Guid.Empty && p.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<MercadoPagoApiCall>(entity =>
        {
            entity.HasKey(call => call.Id);
            entity.HasIndex(call => call.OccurredAt);
            entity.HasIndex(call => call.TenantId);
        });

        modelBuilder.Entity<ErrorLog>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.CorrelationId).HasMaxLength(64);
            entity.Property(row => row.Message).HasMaxLength(400);
            entity.Property(row => row.Path).HasMaxLength(256);
            entity.Property(row => row.Level).HasMaxLength(16);
            entity.HasIndex(row => row.OccurredAt);
        });

        modelBuilder.Entity<IncidentLatch>(entity =>
        {
            entity.HasKey(l => l.Fingerprint);
            entity.Property(l => l.Fingerprint).HasMaxLength(HelpdeskStore.FingerprintMaxLength);
            entity.Property(l => l.ReleaseTag).HasMaxLength(64);
            entity.Property(l => l.SentryId).HasMaxLength(256);
            entity.Property(l => l.PullRequestUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<CommerceHelpMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Alias).HasMaxLength(32);
            entity.Property(m => m.AdminEmail).HasMaxLength(256);
            entity.Property(m => m.Text).HasMaxLength(HelpdeskStore.TextMaxLength);
            entity.HasIndex(m => m.CreatedAt);
            entity.HasQueryFilter(m => _tenant.TenantId != Guid.Empty && m.TenantId == _tenant.TenantId);
        });
    }

    private static HelpArticle SeedArticle(string id, string title, string summary, string body, string keywords, int version)
        => new()
        {
            Id = id,
            Title = title,
            Summary = summary,
            Body = body,
            PublicPath = $"/ayuda/{id}",
            Keywords = keywords,
            CorpusVersion = version,
            Active = true
        };
}
