using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public class InMemoryReconciliationSourceRepository : IReconciliationSourceRepository
{
    private readonly object _lock = new();
    private readonly List<ReconciliationSource> _sources =
    [
        new()
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            Type = ReconciliationSourceType.Branch,
            Code = "BRANCH",
            DisplayName = "Karşılaştırma Dosyası 1",
            Description = "Birinci karşılaştırma kaynağından gelen işlem dosyası.",
            IsActive = true
        },
        new()
        {
            Id = new Guid("22222222-2222-2222-2222-222222222222"),
            Type = ReconciliationSourceType.Bank,
            Code = "BANK",
            DisplayName = "Karşılaştırma Dosyası 2",
            Description = "İkinci karşılaştırma kaynağından gelen işlem dosyası.",
            IsActive = true
        }
    ];

    public IReadOnlyCollection<ReconciliationSource> GetAll()
    {
        lock (_lock)
        {
            return _sources.Select(Clone).ToList();
        }
    }

    public ReconciliationSource? Update(
        Guid id,
        string displayName,
        string description,
        bool isActive)
    {
        lock (_lock)
        {
            var source = _sources.SingleOrDefault(item => item.Id == id);
            if (source is null)
            {
                return null;
            }

            source.DisplayName = displayName;
            source.Description = description;
            source.IsActive = isActive;
            return Clone(source);
        }
    }

    private static ReconciliationSource Clone(ReconciliationSource source)
    {
        return new ReconciliationSource
        {
            Id = source.Id,
            Type = source.Type,
            Code = source.Code,
            DisplayName = source.DisplayName,
            Description = source.Description,
            IsActive = source.IsActive
        };
    }
}
