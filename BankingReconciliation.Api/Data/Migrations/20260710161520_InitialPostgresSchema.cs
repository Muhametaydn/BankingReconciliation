using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BankingReconciliation.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgresSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BranchFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    BankFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    TotalBranchRecords = table.Column<int>(type: "integer", nullable: false),
                    TotalBankRecords = table.Column<int>(type: "integer", nullable: false),
                    MatchedCount = table.Column<int>(type: "integer", nullable: false),
                    OnlyInBranchCount = table.Column<int>(type: "integer", nullable: false),
                    OnlyInBankCount = table.Column<int>(type: "integer", nullable: false),
                    MismatchCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationDifferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BranchCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FundCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TransactionNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BranchTransactionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BranchQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    BranchAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    BankTransactionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BankQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    BankAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    QuantityDifference = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    AmountDifference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationDifferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationDifferences_ReconciliationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ReconciliationBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationBatches_CreatedAt",
                table: "ReconciliationBatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationDifferences_BatchId",
                table: "ReconciliationDifferences",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationDifferences_BatchId_BranchCode_FundCode_Trans~",
                table: "ReconciliationDifferences",
                columns: new[] { "BatchId", "BranchCode", "FundCode", "TransactionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationDifferences_Status",
                table: "ReconciliationDifferences",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationDifferences");

            migrationBuilder.DropTable(
                name: "ReconciliationBatches");
        }
    }
}
