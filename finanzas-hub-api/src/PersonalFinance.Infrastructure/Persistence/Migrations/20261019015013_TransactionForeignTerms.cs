using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionForeignTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "manual_fx_rate",
                schema: "ledger",
                table: "operations",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "original_amount",
                schema: "ledger",
                table: "operations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_currency",
                schema: "ledger",
                table: "operations",
                type: "char(3)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_original_price",
                schema: "ledger",
                table: "operations",
                sql: "(original_amount IS NULL) = (original_currency IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_original_price",
                schema: "ledger",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "manual_fx_rate",
                schema: "ledger",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "original_amount",
                schema: "ledger",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "original_currency",
                schema: "ledger",
                table: "operations");
        }
    }
}
