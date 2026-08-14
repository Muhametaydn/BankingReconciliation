namespace BankingReconciliation.Api.Data;

public class ReconciliationFileSchemaSettingsEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string SchemaJson { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
