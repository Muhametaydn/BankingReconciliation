using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeSourceDisplayNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ReconciliationSources",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Description", "DisplayName" },
                values: new object[] { "Birinci karşılaştırma kaynağından gelen işlem dosyası.", "Karşılaştırma Dosyası 1" });

            migrationBuilder.UpdateData(
                table: "ReconciliationSources",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "Description", "DisplayName" },
                values: new object[] { "İkinci karşılaştırma kaynağından gelen işlem dosyası.", "Karşılaştırma Dosyası 2" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ReconciliationSources",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Description", "DisplayName" },
                values: new object[] { "Sube veya kaynak sistemden gelen islem dosyasi.", "Branch File" });

            migrationBuilder.UpdateData(
                table: "ReconciliationSources",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "Description", "DisplayName" },
                values: new object[] { "Banka veya merkezi sistemden gelen islem dosyasi.", "Bank File" });
        }
    }
}
