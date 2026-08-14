using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BeforeStateJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterStateJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEvents_Action",
                table: "ReconciliationAuditEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEvents_Actor",
                table: "ReconciliationAuditEvents",
                column: "Actor");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEvents_CreatedAt",
                table: "ReconciliationAuditEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEvents_ResourceType",
                table: "ReconciliationAuditEvents",
                column: "ResourceType");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEvents_ResourceType_ResourceId",
                table: "ReconciliationAuditEvents",
                columns: new[] { "ResourceType", "ResourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationAuditEvents");
        }
    }
}
