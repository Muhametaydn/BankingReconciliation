using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public class ReconciliationComparisonOptionsStore
{
    private readonly object _lock = new();
    private ReconciliationComparisonOptions _options;

    public ReconciliationComparisonOptionsStore(IOptions<ReconciliationComparisonOptions> options)
    {
        _options = Clone(options.Value);
    }

    public ReconciliationComparisonOptions GetOptions()
    {
        lock (_lock)
        {
            return Clone(_options);
        }
    }

    public void Update(ReconciliationComparisonOptions options)
    {
        lock (_lock)
        {
            _options = Clone(options);
        }
    }

    internal static ReconciliationComparisonOptions Clone(ReconciliationComparisonOptions options)
    {
        return new ReconciliationComparisonOptions
        {
            NormalizeCodeCase = options.NormalizeCodeCase,
            TrimTextValues = options.TrimTextValues,
            TrimBranchCode = options.TrimBranchCode,
            TrimFundCode = options.TrimFundCode,
            TrimTransactionNumber = options.TrimTransactionNumber,
            RequireExactMatch = options.RequireExactMatch,
            QuantityTolerance = options.QuantityTolerance,
            AmountTolerance = options.AmountTolerance,
            QuantityDecimalPlaces = options.QuantityDecimalPlaces,
            BranchQuantityDecimalPlaces = options.BranchQuantityDecimalPlaces,
            BankQuantityDecimalPlaces = options.BankQuantityDecimalPlaces,
            AmountDecimalPlaces = options.AmountDecimalPlaces,
            BranchAmountDecimalPlaces = options.BranchAmountDecimalPlaces,
            BankAmountDecimalPlaces = options.BankAmountDecimalPlaces,
            MatchingFields = options.MatchingFields?.ToArray() ?? [],
            ComparisonFields = options.ComparisonFields?.ToArray() ?? [],
            ResultFields = options.ResultFields?.ToArray() ?? [],
            BranchCodeMappings = CloneMappings(options.BranchCodeMappings),
            FundCodeMappings = CloneMappings(options.FundCodeMappings),
            TransactionNumberMappings = CloneMappings(options.TransactionNumberMappings),
            FieldMappings = (options.FieldMappings ?? [])
                .ToDictionary(
                    mapping => mapping.Key,
                    mapping => CloneMappings(mapping.Value),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static Dictionary<string, string> CloneMappings(
        IReadOnlyDictionary<string, string>? mappings)
    {
        return mappings?.ToDictionary(
                mapping => mapping.Key,
                mapping => mapping.Value,
                StringComparer.OrdinalIgnoreCase) ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
