using Microsoft.EntityFrameworkCore;
using UsageBilling.Domain.Models;

namespace UsageBilling.Infrastructure.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<BillingAccount> Accounts => Set<BillingAccount>();
    public DbSet<MeterDefinition> Meters => Set<MeterDefinition>();
    public DbSet<UsageRecord> UsageEvents => Set<UsageRecord>();
    public DbSet<UsageAggregate> UsageAggregates => Set<UsageAggregate>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingAccount>(entity =>
        {
            entity.ToTable("billing_accounts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ExternalId).IsUnique();
            entity.Property(x => x.ExternalId).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Plan).HasMaxLength(80);
            entity.Property(x => x.BillingEmail).HasMaxLength(240);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.BaseFee).HasPrecision(18, 4);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        });

        modelBuilder.Entity<MeterDefinition>(entity =>
        {
            entity.ToTable("meter_definitions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Unit).HasMaxLength(40);
            entity.Property(x => x.Aggregation).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.IncludedQuantity).HasPrecision(20, 4);
            entity.Property(x => x.FirstTierLimit).HasPrecision(20, 4);
            entity.Property(x => x.FirstTierPrice).HasPrecision(20, 8);
            entity.Property(x => x.OveragePrice).HasPrecision(20, 8);
            entity.Property(x => x.Currency).HasMaxLength(3);
        });

        modelBuilder.Entity<UsageRecord>(entity =>
        {
            entity.ToTable("usage_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => new { x.AccountId, x.OccurredAt });
            entity.Property(x => x.EventId).HasMaxLength(160);
            entity.Property(x => x.AccountExternalId).HasMaxLength(80);
            entity.Property(x => x.Meter).HasMaxLength(80);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.DimensionsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<UsageAggregate>(entity =>
        {
            entity.ToTable("usage_aggregates");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AccountId, x.Meter, x.PeriodStart }).IsUnique();
            entity.Property(x => x.AccountExternalId).HasMaxLength(80);
            entity.Property(x => x.Meter).HasMaxLength(80);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Number).IsUnique();
            entity.HasIndex(x => new { x.AccountId, x.PeriodStart, x.PeriodEnd }).IsUnique();
            entity.Property(x => x.Number).HasMaxLength(120);
            entity.Property(x => x.AccountExternalId).HasMaxLength(80);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.Tax).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ExternalInvoiceId).HasMaxLength(160);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.ToTable("invoice_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Meter).HasMaxLength(80);
            entity.Property(x => x.Description).HasMaxLength(240);
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(20, 8);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.Property(x => x.Type).HasMaxLength(120);
            entity.Property(x => x.AggregateId).HasMaxLength(160);
            entity.Property(x => x.Payload).HasColumnType("jsonb");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.LastError).HasMaxLength(1000);
        });
    }
}