using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations;

[DbContext(typeof(ReconciliationDbContext))]
[Migration("20260716143000_AddPersistentFileSchemaSettings")]
public partial class AddPersistentFileSchemaSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReconciliationFileSchemaSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                SchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReconciliationFileSchemaSettings", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ReconciliationFileSchemaSettings");
    }
}
