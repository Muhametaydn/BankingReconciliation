using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BankingReconciliation.Api.Services;

public class PostgresReconciliationSourceRepository : IReconciliationSourceRepository
{
    private readonly ReconciliationDbContext _dbContext;

    public PostgresReconciliationSourceRepository(ReconciliationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyCollection<ReconciliationSource> GetAll()
    {
        return _dbContext.ReconciliationSources
            .AsNoTracking()
            .OrderBy(source => source.Type)
            .ThenBy(source => source.Code)
            .Select(source => new ReconciliationSource
            {
                Id = source.Id,
                Type = source.Type,
                Code = source.Code,
                DisplayName = source.DisplayName,
                Description = source.Description,
                IsActive = source.IsActive
            })
            .ToList();
    }

    public ReconciliationSource? Update(
        Guid id,
        string displayName,
        string description,
        bool isActive)
    {
        var entity = _dbContext.ReconciliationSources.Find(id);
        if (entity is null)
        {
            return null;
        }

        entity.DisplayName = displayName;
        entity.Description = description;
        entity.IsActive = isActive;
        _dbContext.SaveChanges();

        return new ReconciliationSource
        {
            Id = entity.Id,
            Type = entity.Type,
            Code = entity.Code,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            IsActive = entity.IsActive
        };
    }
}
