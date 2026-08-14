namespace BankingReconciliation.Api.Contracts;

public class ReconciliationFileValidationResponse
{
    public bool IsValid { get; set; }
    public int RecordCount { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int? RowNumber { get; set; }
    public string? ColumnName { get; set; }
    public List<ReconciliationFileValidationErrorResponse> Errors { get; set; } = [];
}

public class ReconciliationFileValidationErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public int RowNumber { get; set; }
    public string? ColumnName { get; set; }
    public string Rule { get; set; } = string.Empty;
}
