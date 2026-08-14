using BankingReconciliation.Api.Contracts;
using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public static class ReconciliationResponseMapper
{
    public static ReconciliationSummaryResponse ToResponse(this ReconciliationBatch batch)
    {
        var response = batch.Summary.ToResponse();
        response.BatchId = batch.Id;
        response.CreatedAt = batch.CreatedAt;
        response.BatchStatus = batch.Status;
        response.InputType = batch.InputType;
        response.ApprovalStatus = batch.ApprovalStatus;
        response.InitiatedBy = batch.InitiatedBy;
        response.DecisionBy = batch.DecisionBy;
        response.DecisionAt = batch.DecisionAt;
        response.DecisionComment = batch.DecisionComment;
        response.BranchFileName = batch.BranchFileName;
        response.BankFileName = batch.BankFileName;
        response.ProcessingDurationMilliseconds = batch.ProcessingDurationMilliseconds;
        response.AttemptCount = batch.AttemptCount;
        response.LastAttemptAt = batch.LastAttemptAt;
        response.NextAttemptAt = batch.NextAttemptAt;
        response.LeaseExpiresAt = batch.LeaseExpiresAt;
        response.ErrorCode = batch.ErrorCode;
        response.ErrorMessage = batch.ErrorMessage;

        return response;
    }

    public static ReconciliationBatchListItemResponse ToListItemResponse(this ReconciliationBatch batch)
    {
        return new ReconciliationBatchListItemResponse
        {
            Id = batch.Id,
            CreatedAt = batch.CreatedAt,
            Status = batch.Status,
            InputType = batch.InputType,
            ApprovalStatus = batch.ApprovalStatus,
            InitiatedBy = batch.InitiatedBy,
            DecisionBy = batch.DecisionBy,
            DecisionAt = batch.DecisionAt,
            DecisionComment = batch.DecisionComment,
            BranchFileName = batch.BranchFileName,
            BankFileName = batch.BankFileName,
            ProcessingDurationMilliseconds = batch.ProcessingDurationMilliseconds,
            AttemptCount = batch.AttemptCount,
            LastAttemptAt = batch.LastAttemptAt,
            NextAttemptAt = batch.NextAttemptAt,
            LeaseExpiresAt = batch.LeaseExpiresAt,
            ErrorCode = batch.ErrorCode,
            ErrorMessage = batch.ErrorMessage,
            TotalBranchRecords = batch.Summary.TotalBranchRecords,
            TotalBankRecords = batch.Summary.TotalBankRecords,
            MatchedCount = batch.Summary.MatchedCount,
            OnlyInBranchCount = batch.Summary.OnlyInBranchCount,
            OnlyInBankCount = batch.Summary.OnlyInBankCount,
            MismatchCount = batch.Summary.MismatchCount,
            IsExactMatch = IsExactMatch(batch.Summary)
        };
    }

    public static ReconciliationBatchResponse ToDetailResponse(this ReconciliationBatch batch)
    {
        return new ReconciliationBatchResponse
        {
            Id = batch.Id,
            CreatedAt = batch.CreatedAt,
            Status = batch.Status,
            InputType = batch.InputType,
            ApprovalStatus = batch.ApprovalStatus,
            InitiatedBy = batch.InitiatedBy,
            DecisionBy = batch.DecisionBy,
            DecisionAt = batch.DecisionAt,
            DecisionComment = batch.DecisionComment,
            BranchFileName = batch.BranchFileName,
            BankFileName = batch.BankFileName,
            ProcessingDurationMilliseconds = batch.ProcessingDurationMilliseconds,
            AttemptCount = batch.AttemptCount,
            LastAttemptAt = batch.LastAttemptAt,
            NextAttemptAt = batch.NextAttemptAt,
            LeaseExpiresAt = batch.LeaseExpiresAt,
            ErrorCode = batch.ErrorCode,
            ErrorMessage = batch.ErrorMessage,
            TotalBranchRecords = batch.Summary.TotalBranchRecords,
            TotalBankRecords = batch.Summary.TotalBankRecords,
            MatchedCount = batch.Summary.MatchedCount,
            OnlyInBranchCount = batch.Summary.OnlyInBranchCount,
            OnlyInBankCount = batch.Summary.OnlyInBankCount,
            MismatchCount = batch.Summary.MismatchCount,
            IsExactMatch = IsExactMatch(batch.Summary),
            Results = batch.Summary.Results.Select(result => result.ToResponse()).ToList()
        };
    }

    public static ReconciliationSummaryResponse ToResponse(this ReconciliationSummary summary)
    {
        return new ReconciliationSummaryResponse
        {
            TotalBranchRecords = summary.TotalBranchRecords,
            TotalBankRecords = summary.TotalBankRecords,
            MatchedCount = summary.MatchedCount,
            OnlyInBranchCount = summary.OnlyInBranchCount,
            OnlyInBankCount = summary.OnlyInBankCount,
            MismatchCount = summary.MismatchCount,
            IsExactMatch = IsExactMatch(summary),
            Results = summary.Results.Select(result => result.ToResponse()).ToList()
        };
    }

    private static bool IsExactMatch(ReconciliationSummary summary)
    {
        return summary.MismatchCount == 0 &&
            summary.OnlyInBranchCount == 0 &&
            summary.OnlyInBankCount == 0 &&
            summary.TotalBranchRecords == summary.TotalBankRecords;
    }

    private static ReconciliationResultResponse ToResponse(this ReconciliationResult result)
    {
        return new ReconciliationResultResponse
        {
            Status = result.Status,
            BranchCode = result.BranchCode,
            FundCode = result.FundCode,
            TransactionNumber = result.TransactionNumber,
            BranchRecord = result.BranchRecord.ToResponse(),
            BankRecord = result.BankRecord.ToResponse(),
            QuantityDifference = result.QuantityDifference,
            AmountDifference = result.AmountDifference,
            FieldDifferences = new Dictionary<string, decimal>(result.FieldDifferences, StringComparer.OrdinalIgnoreCase),
            FieldValues = CreateResponseFieldValues(result)
        };
    }

    private static Dictionary<string, string> CreateResponseFieldValues(ReconciliationResult result)
    {
        if (result.FieldValues.Count > 0)
        {
            return new Dictionary<string, string>(result.FieldValues, StringComparer.OrdinalIgnoreCase);
        }

        var record = result.BranchRecord ?? result.BankRecord;
        return record is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BranchCode"] = record.BranchCode,
                ["FundCode"] = record.FundCode,
                ["TransactionNumber"] = record.TransactionNumber
            };
    }

    private static TransactionRecordResponse? ToResponse(this TransactionRecord? record)
    {
        if (record is null)
        {
            return null;
        }

        return new TransactionRecordResponse
        {
            BranchCode = record.BranchCode,
            FundCode = record.FundCode,
            TransactionNumber = record.TransactionNumber,
            TransactionDate = record.TransactionDate,
            Quantity = record.Quantity,
            Amount = record.Amount,
            ExtraFields = new Dictionary<string, string>(record.ExtraFields, StringComparer.OrdinalIgnoreCase)
        };
    }
}
