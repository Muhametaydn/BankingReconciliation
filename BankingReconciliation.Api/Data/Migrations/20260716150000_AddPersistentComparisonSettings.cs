using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations;

[DbContext(typeof(ReconciliationDbContext))]
[Migration("20260716150000_AddPersistentComparisonSettings")]
public partial class AddPersistentComparisonSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReconciliationComparisonSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReconciliationComparisonSettings", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ReconciliationComparisonSettings");
    }
}
