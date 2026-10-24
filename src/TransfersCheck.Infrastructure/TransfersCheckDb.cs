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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Merchant>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Ignore(m => m.TenantId);
            entity.Property(m => m.ChecklistStep).HasMaxLength(32);
            entity.Property(m => m.PlanCode).HasMaxLength(32);
            entity.HasQueryFilter(m => _tenant.TenantId != Guid.Empty && m.Id == _tenant.TenantId);
        });

        modelBuilder.Entity<LoginAccount>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Email).HasMaxLength(256);
            entity.Property(a => a.PasswordHash).HasMaxLength(500);
            entity.HasIndex(a => a.Email).IsUnique();
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
            entity.Property(t => t.TypeCode).HasMaxLength(64);
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
    }
}
