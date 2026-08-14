using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations;

[DbContext(typeof(ReconciliationDbContext))]
[Migration("20260716120000_AddDynamicDifferenceJson")]
public partial class AddDynamicDifferenceJson : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BranchExtraFieldsJson",
            table: "ReconciliationDifferences",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BankExtraFieldsJson",
            table: "ReconciliationDifferences",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FieldDifferencesJson",
            table: "ReconciliationDifferences",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BranchExtraFieldsJson",
            table: "ReconciliationDifferences");

        migrationBuilder.DropColumn(
            name: "BankExtraFieldsJson",
            table: "ReconciliationDifferences");

        migrationBuilder.DropColumn(
            name: "FieldDifferencesJson",
            table: "ReconciliationDifferences");
    }
}
