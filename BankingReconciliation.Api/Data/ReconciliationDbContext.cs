using BankingReconciliation.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BankingReconciliation.Api.Data;

public class ReconciliationDbContext : DbContext
{
    public ReconciliationDbContext(DbContextOptions<ReconciliationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReconciliationBatchEntity> ReconciliationBatches => Set<ReconciliationBatchEntity>();
    public DbSet<ReconciliationDifferenceEntity> ReconciliationDifferences => Set<ReconciliationDifferenceEntity>();
    public DbSet<ReconciliationSourceEntity> ReconciliationSources => Set<ReconciliationSourceEntity>();
    public DbSet<ReconciliationFileSchemaSettingsEntity> ReconciliationFileSchemaSettings =>
        Set<ReconciliationFileSchemaSettingsEntity>();
    public DbSet<ReconciliationComparisonSettingsEntity> ReconciliationComparisonSettings =>
        Set<ReconciliationComparisonSettingsEntity>();
    public DbSet<ReconciliationAuditEventEntity> ReconciliationAuditEvents =>
        Set<ReconciliationAuditEventEntity>();
    public DbSet<ReconciliationAuditEventArchiveEntity> ReconciliationAuditEventArchives =>
        Set<ReconciliationAuditEventArchiveEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReconciliationBatchEntity>(entity =>
        {
            entity.ToTable("ReconciliationBatches");
            entity.HasKey(batch => batch.Id);
            entity.Property(batch => batch.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(batch => batch.InputType).HasConversion<string>().HasMaxLength(32);
            entity.Property(batch => batch.ApprovalStatus)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(ReconciliationApprovalStatus.NotApplicable);
            entity.Property(batch => batch.InitiatedBy).HasMaxLength(256);
            entity.Property(batch => batch.DecisionBy).HasMaxLength(200);
            entity.Property(batch => batch.DecisionComment).HasMaxLength(1000);
            entity.Property(batch => batch.BranchFileName).HasMaxLength(260);
            entity.Property(batch => batch.BankFileName).HasMaxLength(260);
            entity.Property(batch => batch.TemporaryStorageKey).HasMaxLength(32);
            entity.Property(batch => batch.ErrorCode).HasMaxLength(80);
            entity.Property(batch => batch.ErrorMessage).HasMaxLength(1000);
            entity.Property(batch => batch.LeaseOwner).HasMaxLength(200);
            entity.HasIndex(batch => batch.CreatedAt);
            entity.HasIndex(batch => batch.Status);
            entity.HasIndex(batch => batch.InputType);
            entity.HasIndex(batch => batch.ApprovalStatus);
            entity.HasIndex(batch => batch.ErrorCode);
            entity.HasIndex(batch => new { batch.Status, batch.InputType, batch.NextAttemptAt });
            entity.HasIndex(batch => new { batch.Status, batch.InputType, batch.LeaseExpiresAt });
            entity.HasIndex(batch => new
            {
                batch.Status,
                batch.InputType,
                batch.TemporaryStorageKey,
                batch.NextAttemptAt
            });
            entity.HasIndex(batch => new
            {
                batch.Status,
                batch.InputType,
                batch.TemporaryStorageKey,
                batch.LeaseExpiresAt
            });
        });

        modelBuilder.Entity<ReconciliationDifferenceEntity>(entity =>
        {
            entity.ToTable("ReconciliationDifferences");
            entity.HasKey(difference => difference.Id);
            entity.Property(difference => difference.Status).HasConversion<string>().HasMaxLength(64);
            entity.Property(difference => difference.BranchCode).HasMaxLength(80);
            entity.Property(difference => difference.FundCode).HasMaxLength(80);
            entity.Property(difference => difference.TransactionNumber).HasMaxLength(120);
            entity.Property(difference => difference.BranchQuantity).HasPrecision(18, 6);
            entity.Property(difference => difference.BranchAmount).HasPrecision(18, 2);
            entity.Property(difference => difference.BankQuantity).HasPrecision(18, 6);
            entity.Property(difference => difference.BankAmount).HasPrecision(18, 2);
            entity.Property(difference => difference.QuantityDifference).HasPrecision(18, 6);
            entity.Property(difference => difference.AmountDifference).HasPrecision(18, 2);
            entity.Property(difference => difference.BranchExtraFieldsJson).HasColumnType("jsonb");
            entity.Property(difference => difference.BankExtraFieldsJson).HasColumnType("jsonb");
            entity.Property(difference => difference.FieldDifferencesJson).HasColumnType("jsonb");
            entity.HasIndex(difference => difference.BatchId);
            entity.HasIndex(difference => difference.Status);
            entity.HasIndex(difference => new
            {
                difference.BatchId,
                difference.BranchCode,
                difference.FundCode,
                difference.TransactionNumber
            }).IsUnique();
            entity.HasOne(difference => difference.Batch)
                .WithMany(batch => batch.Differences)
                .HasForeignKey(difference => difference.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReconciliationSourceEntity>(entity =>
        {
            entity.ToTable("ReconciliationSources");
            entity.HasKey(source => source.Id);
            entity.Property(source => source.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(source => source.Code).HasMaxLength(80);
            entity.Property(source => source.DisplayName).HasMaxLength(160);
            entity.Property(source => source.Description).HasMaxLength(500);
            entity.HasIndex(source => new
            {
                source.Type,
                source.Code
            }).IsUnique();
            entity.HasData(
                new ReconciliationSourceEntity
                {
                    Id = new Guid("11111111-1111-1111-1111-111111111111"),
                    Type = ReconciliationSourceType.Branch,
                    Code = "BRANCH",
                    DisplayName = "Karşılaştırma Dosyası 1",
                    Description = "Birinci karşılaştırma kaynağından gelen işlem dosyası.",
                    IsActive = true
                },
                new ReconciliationSourceEntity
                {
                    Id = new Guid("22222222-2222-2222-2222-222222222222"),
                    Type = ReconciliationSourceType.Bank,
                    Code = "BANK",
                    DisplayName = "Karşılaştırma Dosyası 2",
                    Description = "İkinci karşılaştırma kaynağından gelen işlem dosyası.",
                    IsActive = true
                });
        });

        modelBuilder.Entity<ReconciliationFileSchemaSettingsEntity>(entity =>
        {
            entity.ToTable("ReconciliationFileSchemaSettings");
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.Id).ValueGeneratedNever();
            entity.Property(settings => settings.SchemaJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ReconciliationComparisonSettingsEntity>(entity =>
        {
            entity.ToTable("ReconciliationComparisonSettings");
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.Id).ValueGeneratedNever();
            entity.Property(settings => settings.OptionsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ReconciliationAuditEventEntity>(entity =>
        {
            entity.ToTable("ReconciliationAuditEvents");
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.Actor).HasMaxLength(200);
            entity.Property(auditEvent => auditEvent.Action).HasConversion<string>().HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.ResourceType).HasConversion<string>().HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.ResourceId).HasMaxLength(200);
            entity.Property(auditEvent => auditEvent.BeforeStateJson).HasColumnType("jsonb");
            entity.Property(auditEvent => auditEvent.AfterStateJson).HasColumnType("jsonb");
            entity.HasIndex(auditEvent => auditEvent.CreatedAt);
            entity.HasIndex(auditEvent => auditEvent.Actor);
            entity.HasIndex(auditEvent => auditEvent.Action);
            entity.HasIndex(auditEvent => auditEvent.ResourceType);
            entity.HasIndex(auditEvent => new { auditEvent.ResourceType, auditEvent.ResourceId });
        });

        modelBuilder.Entity<ReconciliationAuditEventArchiveEntity>(entity =>
        {
            entity.ToTable("ReconciliationAuditEventArchives");
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.Actor).HasMaxLength(200);
            entity.Property(auditEvent => auditEvent.Action).HasConversion<string>().HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.ResourceType).HasConversion<string>().HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.ResourceId).HasMaxLength(200);
            entity.Property(auditEvent => auditEvent.BeforeStateJson).HasColumnType("jsonb");
            entity.Property(auditEvent => auditEvent.AfterStateJson).HasColumnType("jsonb");
            entity.Property(auditEvent => auditEvent.IntegrityHash).HasMaxLength(64);
            entity.Property(auditEvent => auditEvent.ExternalArchiveKey).HasMaxLength(1024);
            entity.HasIndex(auditEvent => auditEvent.CreatedAt);
            entity.HasIndex(auditEvent => auditEvent.ArchivedAt);
            entity.HasIndex(auditEvent => auditEvent.ExternalArchivedAt);
            entity.HasIndex(auditEvent => auditEvent.Actor);
            entity.HasIndex(auditEvent => auditEvent.Action);
            entity.HasIndex(auditEvent => auditEvent.ResourceType);
            entity.HasIndex(auditEvent => new { auditEvent.ResourceType, auditEvent.ResourceId });
        });
    }
}
