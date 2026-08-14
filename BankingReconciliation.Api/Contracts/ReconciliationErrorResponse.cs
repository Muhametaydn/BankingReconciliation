namespace BankingReconciliation.Api.Contracts;

public class ReconciliationErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? RowNumber { get; set; }
    public string? ColumnName { get; set; }
    public string? SourceName { get; set; }
    public string? MatchingKey { get; set; }
}
