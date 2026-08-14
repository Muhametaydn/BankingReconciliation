namespace BankingReconciliation.Api.Services;

public sealed record ReconciliationStreamedUpload(
    string BranchFileName,
    string BankFileName);
