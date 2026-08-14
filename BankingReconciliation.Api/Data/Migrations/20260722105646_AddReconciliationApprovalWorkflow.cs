using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "ReconciliationBatches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecisionAt",
                table: "ReconciliationBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionBy",
                table: "ReconciliationBatches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionComment",
                table: "ReconciliationBatches",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"ReconciliationBatches\" SET \"ApprovalStatus\" = 'Pending' WHERE \"Status\" = 'Completed';");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_ApprovalStatus",
                table: "ReconciliationBatches",
                column: "ApprovalStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconciliationBatches_ApprovalStatus",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "DecisionAt",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "DecisionBy",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "DecisionComment",
                table: "ReconciliationBatches");
        }
    }
}
