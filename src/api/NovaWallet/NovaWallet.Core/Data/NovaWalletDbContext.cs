using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Core.Entities;

namespace NovaWallet.Core.Data;

public class NovaWalletDbContext(DbContextOptions<NovaWalletDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<TransferIdempotencyRecord> TransferIdempotencyRecords => Set<TransferIdempotencyRecord>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<DailyOutboundUsage> DailyOutboundUsages => Set<DailyOutboundUsage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(e =>
        {
            e.HasKey(w => w.WalletId);
            e.HasIndex(w => w.CustomerId).IsUnique();
        });

        modelBuilder.Entity<LedgerTransaction>(e =>
        {
            e.HasKey(t => t.TransactionId);
            e.HasIndex(t => new { t.WalletId, t.CreatedAtUtc });
        });

        modelBuilder.Entity<TransferIdempotencyRecord>(e =>
        {
            e.HasKey(r => r.IdempotencyKey);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(a => a.AuditId);
            e.HasIndex(a => a.WalletId);
        });

        modelBuilder.Entity<DailyOutboundUsage>(e =>
        {
            e.HasKey(d => new { d.WalletId, d.UsageDateWat });
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.HasKey(o => o.OutboxMessageId);
            e.HasIndex(o => o.ProcessedAtUtc);
        });
    }

    /// <summary>
    /// Defense-in-depth: validates DataAnnotations on every added/modified entity before it
    /// reaches the database, independent of DTO validation at the API boundary (NFR-ARCH-8).
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTrackedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateTrackedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateTrackedEntities()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var validationContext = new ValidationContext(entry.Entity);
            Validator.ValidateObject(entry.Entity, validationContext, validateAllProperties: true);
        }
    }
}
