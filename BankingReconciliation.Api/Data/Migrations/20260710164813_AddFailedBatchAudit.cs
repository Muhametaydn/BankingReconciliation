using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFailedBatchAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "ReconciliationBatches",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "ReconciliationBatches",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_ErrorCode",
                table: "ReconciliationBatches",
                column: "ErrorCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconciliationBatches_ErrorCode",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "ReconciliationBatches");
        }
    }
}
