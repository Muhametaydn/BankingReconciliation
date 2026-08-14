using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableAuditArchiveTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalArchiveKey",
                table: "ReconciliationAuditEventArchives",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalArchivedAt",
                table: "ReconciliationAuditEventArchives",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEventArchives_ExternalArchivedAt",
                table: "ReconciliationAuditEventArchives",
                column: "ExternalArchivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReconciliationAuditEventArchives_ExternalArchivedAt",
                table: "ReconciliationAuditEventArchives");

            migrationBuilder.DropColumn(
                name: "ExternalArchiveKey",
                table: "ReconciliationAuditEventArchives");

            migrationBuilder.DropColumn(
                name: "ExternalArchivedAt",
                table: "ReconciliationAuditEventArchives");
        }
    }
}
