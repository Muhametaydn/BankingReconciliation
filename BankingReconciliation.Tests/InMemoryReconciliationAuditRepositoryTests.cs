using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class InMemoryReconciliationAuditRepositoryTests
{
    [Fact]
    public void GetAll_FiltersActorActionAndDate_AndReturnsNewestFirst()
    {
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryReconciliationAuditRepository(timeProvider);
        repository.Add(
            ReconciliationAuditAction.SourceUpdated,
            "admin-1",
            ReconciliationAuditResourceType.ReconciliationSource,
            "source-1",
            new { DisplayName = "Before" },
            new { DisplayName = "After" });

        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(10);
        repository.Add(
            ReconciliationAuditAction.FileSchemaUpdated,
            "admin-2",
            ReconciliationAuditResourceType.FileSchema,
            "active",
            null,
            new { Columns = 6 });

        var events = repository.GetAll(new ReconciliationAuditQuery
        {
            Actor = "ADMIN-2",
            Action = ReconciliationAuditAction.FileSchemaUpdated,
            From = new DateTimeOffset(2026, 7, 22, 8, 5, 0, TimeSpan.Zero),
            Take = 10
        });
        var auditEvent = Assert.Single(events);

        Assert.Equal("admin-2", auditEvent.Actor);
        Assert.Contains("\"columns\":6", auditEvent.AfterStateJson);
        Assert.Equal(1, repository.Count(new ReconciliationAuditQuery
        {
            ResourceType = ReconciliationAuditResourceType.FileSchema
        }));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
