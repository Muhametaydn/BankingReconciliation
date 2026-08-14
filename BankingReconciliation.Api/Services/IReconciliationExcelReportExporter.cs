using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationExcelReportExporter
{
    byte[] ExportDifferences(ReconciliationBatch batch);
}
