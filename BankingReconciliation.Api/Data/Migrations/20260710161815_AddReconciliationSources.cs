using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationSources", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ReconciliationSources",
                columns: new[] { "Id", "Code", "Description", "DisplayName", "IsActive", "Type" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "BRANCH", "Sube veya kaynak sistemden gelen islem dosyasi.", "Branch File", true, "Branch" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "BANK", "Banka veya merkezi sistemden gelen islem dosyasi.", "Bank File", true, "Bank" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationSources_Type_Code",
                table: "ReconciliationSources",
                columns: new[] { "Type", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationSources");
        }
    }
}
