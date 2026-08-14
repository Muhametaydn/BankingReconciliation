using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public class ReconciliationDatabaseSourceConfiguration : IReconciliationDatabaseSourceConfiguration
{
    private readonly IConfiguration _configuration;
    private readonly ReconciliationDatabaseSourcesOptions _options;

    public ReconciliationDatabaseSourceConfiguration(
        IConfiguration configuration,
        IOptions<ReconciliationDatabaseSourcesOptions> options)
    {
        _configuration = configuration;
        _options = options.Value;
    }

    public bool IsConfigured(string sourceCode)
    {
        var source = _options.Sources.SingleOrDefault(item =>
            string.Equals(item.Code, sourceCode, StringComparison.OrdinalIgnoreCase));

        return source is not null &&
            !string.IsNullOrWhiteSpace(
                _configuration.GetConnectionString(source.ConnectionStringName));
    }
}
