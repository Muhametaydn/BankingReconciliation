namespace BankingReconciliation.Api.Data;

public class ReconciliationComparisonSettingsEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string OptionsJson { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
