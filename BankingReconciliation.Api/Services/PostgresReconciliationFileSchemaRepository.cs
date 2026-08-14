using System.Text.Json;
using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

public class PostgresReconciliationFileSchemaRepository : IReconciliationFileSchemaRepository
{
    private readonly ReconciliationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PostgresReconciliationFileSchemaRepository(
        ReconciliationDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public ReconciliationFileSchemaOptions? Get()
    {
        var entity = _dbContext.ReconciliationFileSchemaSettings
            .Find(ReconciliationFileSchemaSettingsEntity.SingletonId);

        return entity is null
            ? null
            : JsonSerializer.Deserialize<ReconciliationFileSchemaOptions>(entity.SchemaJson);
    }

    public void Save(ReconciliationFileSchemaOptions options)
    {
        var entity = _dbContext.ReconciliationFileSchemaSettings
            .Find(ReconciliationFileSchemaSettingsEntity.SingletonId);
        var schemaJson = JsonSerializer.Serialize(ReconciliationFileSchemaStore.Clone(options));

        if (entity is null)
        {
            entity = new ReconciliationFileSchemaSettingsEntity();
            _dbContext.ReconciliationFileSchemaSettings.Add(entity);
        }

        entity.SchemaJson = schemaJson;
        entity.UpdatedAt = _timeProvider.GetUtcNow();
        _dbContext.SaveChanges();
    }
}
