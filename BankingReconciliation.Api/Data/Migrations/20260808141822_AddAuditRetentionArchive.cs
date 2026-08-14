using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditRetentionArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationAuditEventArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BeforeStateJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterStateJson = table.Column<string>(type: "jsonb", nullable: true),
                    IntegrityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationAuditEventArchives", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEventArchives_Action",
                table: "ReconciliationAuditEventArchives",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEventArchives_Actor",
                table: "ReconciliationAuditEventArchives",
                column: "Actor");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEventArchives_ArchivedAt",
                table: "ReconciliationAuditEventArchives",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEventArchives_CreatedAt",
                table: "ReconciliationAuditEventArchives",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEventArchives_ResourceType",
                table: "ReconciliationAuditEventArchives",
                column: "ResourceType");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAuditEventArchives_ResourceType_ResourceId",
                table: "ReconciliationAuditEventArchives",
                columns: new[] { "ResourceType", "ResourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationAuditEventArchives");
        }
    }
}
