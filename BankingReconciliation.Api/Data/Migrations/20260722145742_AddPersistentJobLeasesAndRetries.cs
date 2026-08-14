using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentJobLeasesAndRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "ReconciliationBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                table: "ReconciliationBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "ReconciliationBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "ReconciliationBatches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "ReconciliationBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_Status_InputType_LeaseExpiresAt",
                table: "ReconciliationBatches",
                columns: new[] { "Status", "InputType", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_Status_InputType_NextAttemptAt",
                table: "ReconciliationBatches",
                columns: new[] { "Status", "InputType", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconciliationBatches_Status_InputType_LeaseExpiresAt",
                table: "ReconciliationBatches");

            migrationBuilder.DropIndex(
                name: "IX_ReconciliationBatches_Status_InputType_NextAttemptAt",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "ReconciliationBatches");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "ReconciliationBatches");
        }
    }
}
