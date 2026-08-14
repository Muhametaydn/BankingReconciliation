using System.Text.Json;
using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

public class PostgresReconciliationComparisonOptionsRepository : IReconciliationComparisonOptionsRepository
{
    private readonly ReconciliationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PostgresReconciliationComparisonOptionsRepository(
        ReconciliationDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public ReconciliationComparisonOptions? Get()
    {
        var entity = _dbContext.ReconciliationComparisonSettings
            .Find(ReconciliationComparisonSettingsEntity.SingletonId);

        return entity is null
            ? null
            : JsonSerializer.Deserialize<ReconciliationComparisonOptions>(entity.OptionsJson);
    }

    public void Save(ReconciliationComparisonOptions options)
    {
        var entity = _dbContext.ReconciliationComparisonSettings
            .Find(ReconciliationComparisonSettingsEntity.SingletonId);
        var optionsJson = JsonSerializer.Serialize(ReconciliationComparisonOptionsStore.Clone(options));

        if (entity is null)
        {
            entity = new ReconciliationComparisonSettingsEntity();
            _dbContext.ReconciliationComparisonSettings.Add(entity);
        }

        entity.OptionsJson = optionsJson;
        entity.UpdatedAt = _timeProvider.GetUtcNow();
        _dbContext.SaveChanges();
    }
}
