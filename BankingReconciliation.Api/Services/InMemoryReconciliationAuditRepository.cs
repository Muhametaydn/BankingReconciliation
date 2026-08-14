using System.Text.Json;
using System.Text.Json.Serialization;
using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public class InMemoryReconciliationAuditRepository : IReconciliationAuditRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly object _lock = new();
    private readonly List<ReconciliationAuditEvent> _events = [];
    private readonly List<ReconciliationAuditEvent> _archivedEvents = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryReconciliationAuditRepository(TimeProvider timeProvider)
    {
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
        var auditEvent = new ReconciliationAuditEvent
        {
            Id = Guid.NewGuid(),
            CreatedAt = _timeProvider.GetUtcNow(),
            Actor = actor,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BeforeStateJson = Serialize(beforeState),
            AfterStateJson = Serialize(afterState)
        };

        lock (_lock)
        {
            _events.Add(auditEvent);
        }

        return auditEvent;
    }

    public IReadOnlyCollection<ReconciliationAuditEvent> GetAll(ReconciliationAuditQuery? query = null)
    {
        query ??= new ReconciliationAuditQuery();
        lock (_lock)
        {
            return _events
                .Concat(_archivedEvents)
                .Where(item => MatchesQuery(item, query))
                .OrderByDescending(item => item.CreatedAt)
                .Skip(query.Skip)
                .Take(query.Take)
                .ToList();
        }
    }

    public int Count(ReconciliationAuditQuery? query = null)
    {
        query ??= new ReconciliationAuditQuery();
        lock (_lock)
        {
            return _events.Concat(_archivedEvents).Count(item => MatchesQuery(item, query));
        }
    }

    public Task<ReconciliationAuditRetentionResult> ArchiveAndPurgeAsync(
        DateTimeOffset hotCutoff,
        DateTimeOffset? archiveCutoff,
        int batchSize,
        bool requireExternalArchive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var archivedAt = _timeProvider.GetUtcNow();
            var candidates = _events
                .Where(item => item.CreatedAt < hotCutoff)
                .OrderBy(item => item.CreatedAt)
                .Take(batchSize)
                .ToList();
            foreach (var item in candidates)
            {
                item.ArchivedAt = archivedAt;
                item.IntegrityHash = ReconciliationAuditIntegrity.ComputeHash(item);
                item.IntegrityVerified = true;
                _events.Remove(item);
                _archivedEvents.Add(item);
            }

            List<ReconciliationAuditEvent> purgeCandidates = archiveCutoff is null
                ? []
                : _archivedEvents
                    .Where(item => item.CreatedAt < archiveCutoff &&
                        (!requireExternalArchive || item.ExternalArchivedAt is not null))
                    .OrderBy(item => item.CreatedAt)
                    .Take(batchSize)
                    .ToList();
            foreach (var item in purgeCandidates)
            {
                _archivedEvents.Remove(item);
            }

            return Task.FromResult(new ReconciliationAuditRetentionResult(
                candidates.Count,
                purgeCandidates.Count));
        }
    }

    public IReadOnlyCollection<ReconciliationAuditEvent> GetPendingExternalArchive(int take)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        lock (_lock)
        {
            return _archivedEvents
                .Where(item => item.ExternalArchivedAt is null)
                .OrderBy(item => item.CreatedAt)
                .Take(take)
                .ToList();
        }
    }

    public void MarkExternalArchived(
        IReadOnlyCollection<Guid> eventIds,
        string objectKey,
        DateTimeOffset archivedAt)
    {
        var ids = eventIds.ToHashSet();
        lock (_lock)
        {
            foreach (var item in _archivedEvents.Where(item => ids.Contains(item.Id)))
            {
                item.ExternalArchiveKey = objectKey;
                item.ExternalArchivedAt = archivedAt;
            }
        }
    }

    public ReconciliationAuditStorageStatistics GetStorageStatistics()
    {
        lock (_lock)
        {
            var pending = _archivedEvents
                .Where(item => item.ExternalArchivedAt is null)
                .ToList();
            return new ReconciliationAuditStorageStatistics(
                _events.Count,
                _archivedEvents.Count,
                pending.Count,
                pending.Count == 0 ? null : pending.Min(item => item.CreatedAt));
        }
    }

    internal static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);

    private static bool MatchesQuery(ReconciliationAuditEvent item, ReconciliationAuditQuery query)
    {
        return (query.From is null || item.CreatedAt >= query.From) &&
            (query.To is null || item.CreatedAt <= query.To) &&
            (query.Action is null || item.Action == query.Action) &&
            (query.ResourceType is null || item.ResourceType == query.ResourceType) &&
            (string.IsNullOrWhiteSpace(query.Actor) ||
                item.Actor.Contains(query.Actor.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
