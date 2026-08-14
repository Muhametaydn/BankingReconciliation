using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedFileStorageAffinity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TemporaryStorageKey",
                table: "ReconciliationBatches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ReconciliationBatches"
                SET "Status" = 'Failed',
                    "ApprovalStatus" = 'NotApplicable',
                    "ErrorCode" = 'TemporaryStorageAffinityUnavailable',
                    "ErrorMessage" = 'The uploaded-file job predates storage affinity and must be submitted again.',
                    "NextAttemptAt" = NULL,
                    "LeaseOwner" = NULL,
                    "LeaseExpiresAt" = NULL
                WHERE "InputType" = 'UploadedFiles'
                  AND "Status" IN ('Queued', 'Processing')
                  AND "TemporaryStorageKey" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_Status_InputType_TemporaryStorageKey_~",
                table: "ReconciliationBatches",
                columns: new[] { "Status", "InputType", "TemporaryStorageKey", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_Status_InputType_TemporaryStorageKey~1",
                table: "ReconciliationBatches",
                columns: new[] { "Status", "InputType", "TemporaryStorageKey", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconciliationBatches_Status_InputType_TemporaryStorageKey_~",
                table: "ReconciliationBatches");

            migrationBuilder.DropIndex(
                name: "IX_ReconciliationBatches_Status_InputType_TemporaryStorageKey~1",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "TemporaryStorageKey",
                table: "ReconciliationBatches");
        }
    }
}
