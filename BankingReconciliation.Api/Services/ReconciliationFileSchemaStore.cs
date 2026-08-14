using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public class ReconciliationFileSchemaStore
{
    private readonly object _lock = new();
    private ReconciliationFileSchemaOptions _options;

    public ReconciliationFileSchemaStore(IOptions<ReconciliationFileSchemaOptions> options)
    {
        _options = Clone(options.Value);
    }

    public ReconciliationFileSchemaOptions GetOptions()
    {
        lock (_lock)
        {
            return Clone(_options);
        }
    }

    public void Update(ReconciliationFileSchemaOptions options)
    {
        lock (_lock)
        {
            _options = Clone(options);
        }
    }

    internal static ReconciliationFileSchemaOptions Clone(ReconciliationFileSchemaOptions options)
    {
        return new ReconciliationFileSchemaOptions
        {
            Columns = options.GetEffectiveColumns()
                .Select(column => new ReconciliationFileSchemaColumnOptions
                {
                    Field = column.Field,
                    Name = column.Name,
                    Type = column.Type,
                    Required = column.Required,
                    DateFormat = column.DateFormat,
                    Pattern = column.Pattern,
                    PatternDescription = column.PatternDescription,
                    MinLength = column.MinLength,
                    MaxLength = column.MaxLength,
                    MinValue = column.MinValue,
                    MaxValue = column.MaxValue,
                    MaxDecimalPlaces = column.MaxDecimalPlaces,
                    FixedWidthStart = column.FixedWidthStart,
                    FixedWidthLength = column.FixedWidthLength,
                    AllowedValues = column.AllowedValues.ToArray(),
                    Description = column.Description
                })
                .ToArray()
        };
    }
}
