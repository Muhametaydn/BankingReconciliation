using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationInputType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputType",
                table: "ReconciliationBatches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "UploadedFiles");

            migrationBuilder.Sql(
                """
                UPDATE "ReconciliationBatches"
                SET "InputType" = 'DatabaseSources'
                WHERE "BranchFileName" = 'database:BRANCH'
                  AND "BankFileName" = 'database:BANK';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_InputType",
                table: "ReconciliationBatches",
                column: "InputType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconciliationBatches_InputType",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "InputType",
                table: "ReconciliationBatches");
        }
    }
}
