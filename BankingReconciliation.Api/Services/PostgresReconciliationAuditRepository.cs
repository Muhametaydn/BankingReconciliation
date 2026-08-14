using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BankingReconciliation.Api.Services;

public class PostgresReconciliationAuditRepository : IReconciliationAuditRepository
{
    private readonly ReconciliationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PostgresReconciliationAuditRepository(
        ReconciliationDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public ReconciliationAuditEvent Add(
        ReconciliationAuditAction action,
        string actor,
        ReconciliationAuditResourceType resourceType,
        string resourceId,
        object? beforeState,
        object? afterState)
    {
        var entity = new ReconciliationAuditEventEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = _timeProvider.GetUtcNow(),
            Actor = actor,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BeforeStateJson = InMemoryReconciliationAuditRepository.Serialize(beforeState),
            AfterStateJson = InMemoryReconciliationAuditRepository.Serialize(afterState)
        };

        _dbContext.ReconciliationAuditEvents.Add(entity);
        _dbContext.SaveChanges();
        return ToModel(entity);
    }

    public IReadOnlyCollection<ReconciliationAuditEvent> GetAll(ReconciliationAuditQuery? query = null)
    {
        query ??= new ReconciliationAuditQuery();
        return GetFilteredRecords(query)
            .OrderByDescending(item => item.CreatedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .AsEnumerable()
            .Select(ToModel)
            .ToList();
    }

    public int Count(ReconciliationAuditQuery? query = null)
    {
        query ??= new ReconciliationAuditQuery();
        return GetFilteredRecords(query).Count();
    }

    public async Task<ReconciliationAuditRetentionResult> ArchiveAndPurgeAsync(
        DateTimeOffset hotCutoff,
        DateTimeOffset? archiveCutoff,
        int batchSize,
        bool requireExternalArchive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var events = await _dbContext.ReconciliationAuditEvents
            .FromSqlInterpolated(
                $"""
                SELECT * FROM "ReconciliationAuditEvents"
                WHERE "CreatedAt" < {hotCutoff}
                ORDER BY "CreatedAt"
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        var archivedAt = _timeProvider.GetUtcNow();
        foreach (var entity in events)
        {
            var model = ToModel(entity);
            _dbContext.ReconciliationAuditEventArchives.Add(
                new ReconciliationAuditEventArchiveEntity
                {
                    Id = entity.Id,
                    CreatedAt = entity.CreatedAt,
                    ArchivedAt = archivedAt,
                    Actor = entity.Actor,
                    Action = entity.Action,
                    ResourceType = entity.ResourceType,
                    ResourceId = entity.ResourceId,
                    BeforeStateJson = entity.BeforeStateJson,
                    AfterStateJson = entity.AfterStateJson,
                    IntegrityHash = ReconciliationAuditIntegrity.ComputeHash(model)
                });
        }
        _dbContext.ReconciliationAuditEvents.RemoveRange(events);

        List<ReconciliationAuditEventArchiveEntity> purgeCandidates = [];
        if (archiveCutoff is not null)
        {
            purgeCandidates = requireExternalArchive
                ? await _dbContext.ReconciliationAuditEventArchives
                    .FromSqlInterpolated(
                        $"""
                        SELECT * FROM "ReconciliationAuditEventArchives"
                        WHERE "CreatedAt" < {archiveCutoff.Value}
                          AND "ExternalArchivedAt" IS NOT NULL
                        ORDER BY "CreatedAt"
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED
                        """)
                    .ToListAsync(cancellationToken)
                : await _dbContext.ReconciliationAuditEventArchives
                    .FromSqlInterpolated(
                        $"""
                        SELECT * FROM "ReconciliationAuditEventArchives"
                        WHERE "CreatedAt" < {archiveCutoff.Value}
                        ORDER BY "CreatedAt"
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED
                        """)
                    .ToListAsync(cancellationToken);
            _dbContext.ReconciliationAuditEventArchives.RemoveRange(purgeCandidates);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReconciliationAuditRetentionResult(events.Count, purgeCandidates.Count);
    }

    public IReadOnlyCollection<ReconciliationAuditEvent> GetPendingExternalArchive(int take)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        return _dbContext.ReconciliationAuditEventArchives
            .AsNoTracking()
            .Where(item => item.ExternalArchivedAt == null)
            .OrderBy(item => item.CreatedAt)
            .Take(take)
            .AsEnumerable()
            .Select(ToModel)
            .ToList();
    }

    public void MarkExternalArchived(
        IReadOnlyCollection<Guid> eventIds,
        string objectKey,
        DateTimeOffset archivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (eventIds.Count == 0)
        {
            return;
        }

        var ids = eventIds.ToHashSet();
        var events = _dbContext.ReconciliationAuditEventArchives
            .Where(item => ids.Contains(item.Id) && item.ExternalArchivedAt == null)
            .ToList();
        foreach (var item in events)
        {
            item.ExternalArchiveKey = objectKey;
            item.ExternalArchivedAt = archivedAt;
        }
        _dbContext.SaveChanges();
    }

    public ReconciliationAuditStorageStatistics GetStorageStatistics()
    {
        var pending = _dbContext.ReconciliationAuditEventArchives
            .AsNoTracking()
            .Where(item => item.ExternalArchivedAt == null);
        return new ReconciliationAuditStorageStatistics(
            _dbContext.ReconciliationAuditEvents.AsNoTracking().Count(),
            _dbContext.ReconciliationAuditEventArchives.AsNoTracking().Count(),
            pending.Count(),
            pending.Select(item => (DateTimeOffset?)item.CreatedAt).Min());
    }

    private IQueryable<AuditRecord> GetFilteredRecords(ReconciliationAuditQuery query)
    {
        var active = ApplyFilters(
                _dbContext.ReconciliationAuditEvents.AsNoTracking(),
                query)
            .Select(item => new AuditRecord
            {
                Id = item.Id,
                CreatedAt = item.CreatedAt,
                Actor = item.Actor,
                Action = item.Action,
                ResourceType = item.ResourceType,
                ResourceId = item.ResourceId,
                BeforeStateJson = item.BeforeStateJson,
                AfterStateJson = item.AfterStateJson,
                ArchivedAt = null,
                IntegrityHash = null,
                ExternalArchivedAt = null,
                ExternalArchiveKey = null
            });
        var archived = ApplyArchiveFilters(
                _dbContext.ReconciliationAuditEventArchives.AsNoTracking(),
                query)
            .Select(item => new AuditRecord
            {
                Id = item.Id,
                CreatedAt = item.CreatedAt,
                Actor = item.Actor,
                Action = item.Action,
                ResourceType = item.ResourceType,
                ResourceId = item.ResourceId,
                BeforeStateJson = item.BeforeStateJson,
                AfterStateJson = item.AfterStateJson,
                ArchivedAt = item.ArchivedAt,
                IntegrityHash = item.IntegrityHash,
                ExternalArchivedAt = item.ExternalArchivedAt,
                ExternalArchiveKey = item.ExternalArchiveKey
            });

        return active.Concat(archived);
    }

    private static IQueryable<ReconciliationAuditEventEntity> ApplyFilters(
        IQueryable<ReconciliationAuditEventEntity> events,
        ReconciliationAuditQuery query)
    {
        if (query.From is not null)
        {
            events = events.Where(item => item.CreatedAt >= query.From);
        }
        if (query.To is not null)
        {
            events = events.Where(item => item.CreatedAt <= query.To);
        }
        if (query.Action is not null)
        {
            events = events.Where(item => item.Action == query.Action);
        }
        if (query.ResourceType is not null)
        {
            events = events.Where(item => item.ResourceType == query.ResourceType);
        }
        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            var pattern = $"%{EscapeLikePattern(query.Actor.Trim())}%";
            events = events.Where(item => EF.Functions.ILike(item.Actor, pattern, "\\"));
        }

        return events;
    }

    private static IQueryable<ReconciliationAuditEventArchiveEntity> ApplyArchiveFilters(
        IQueryable<ReconciliationAuditEventArchiveEntity> events,
        ReconciliationAuditQuery query)
    {
        if (query.From is not null)
        {
            events = events.Where(item => item.CreatedAt >= query.From);
        }
        if (query.To is not null)
        {
            events = events.Where(item => item.CreatedAt <= query.To);
        }
        if (query.Action is not null)
        {
            events = events.Where(item => item.Action == query.Action);
        }
        if (query.ResourceType is not null)
        {
            events = events.Where(item => item.ResourceType == query.ResourceType);
        }
        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            var pattern = $"%{EscapeLikePattern(query.Actor.Trim())}%";
            events = events.Where(item => EF.Functions.ILike(item.Actor, pattern, "\\"));
        }

        return events;
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static ReconciliationAuditEvent ToModel(ReconciliationAuditEventEntity entity)
    {
        return new ReconciliationAuditEvent
        {
            Id = entity.Id,
            CreatedAt = entity.CreatedAt,
            Actor = entity.Actor,
            Action = entity.Action,
            ResourceType = entity.ResourceType,
            ResourceId = entity.ResourceId,
            BeforeStateJson = entity.BeforeStateJson,
            AfterStateJson = entity.AfterStateJson
        };
    }

    private static ReconciliationAuditEvent ToModel(AuditRecord record)
    {
        var model = new ReconciliationAuditEvent
        {
            Id = record.Id,
            CreatedAt = record.CreatedAt,
            Actor = record.Actor,
            Action = record.Action,
            ResourceType = record.ResourceType,
            ResourceId = record.ResourceId,
            BeforeStateJson = record.BeforeStateJson,
            AfterStateJson = record.AfterStateJson,
            ArchivedAt = record.ArchivedAt,
            IntegrityHash = record.IntegrityHash,
            ExternalArchivedAt = record.ExternalArchivedAt,
            ExternalArchiveKey = record.ExternalArchiveKey
        };
        if (record.ArchivedAt is not null)
        {
            model.IntegrityVerified = string.Equals(
                record.IntegrityHash,
                ReconciliationAuditIntegrity.ComputeHash(model),
                StringComparison.OrdinalIgnoreCase);
        }

        return model;
    }

    private sealed class AuditRecord
    {
        public Guid Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string Actor { get; set; } = string.Empty;
        public ReconciliationAuditAction Action { get; set; }
        public ReconciliationAuditResourceType ResourceType { get; set; }
        public string ResourceId { get; set; } = string.Empty;
        public string? BeforeStateJson { get; set; }
        public string? AfterStateJson { get; set; }
        public DateTimeOffset? ArchivedAt { get; set; }
        public string? IntegrityHash { get; set; }
        public DateTimeOffset? ExternalArchivedAt { get; set; }
        public string? ExternalArchiveKey { get; set; }
    }

    private static ReconciliationAuditEvent ToModel(
        ReconciliationAuditEventArchiveEntity entity)
    {
        var model = new ReconciliationAuditEvent
        {
            Id = entity.Id,
            CreatedAt = entity.CreatedAt,
            Actor = entity.Actor,
            Action = entity.Action,
            ResourceType = entity.ResourceType,
            ResourceId = entity.ResourceId,
            BeforeStateJson = entity.BeforeStateJson,
            AfterStateJson = entity.AfterStateJson,
            ArchivedAt = entity.ArchivedAt,
            IntegrityHash = entity.IntegrityHash,
            ExternalArchivedAt = entity.ExternalArchivedAt,
            ExternalArchiveKey = entity.ExternalArchiveKey
        };
        model.IntegrityVerified = string.Equals(
            entity.IntegrityHash,
            ReconciliationAuditIntegrity.ComputeHash(model),
            StringComparison.OrdinalIgnoreCase);
        return model;
    }
}
