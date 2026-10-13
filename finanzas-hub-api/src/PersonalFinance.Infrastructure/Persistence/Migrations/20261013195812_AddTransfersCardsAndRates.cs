using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfersCardsAndRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts");

            // Written by hand: the accounts that already exist count in the net worth, as every account did until now.
            migrationBuilder.AddColumn<bool>(
                name: "include_in_net_worth",
                schema: "ledger",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "credit_card_profiles",
                schema: "ledger",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    payment_day = table.Column<int>(type: "integer", nullable: false),
                    closing_day = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_card_profiles", x => x.account_id);
                    table.CheckConstraint("ck_credit_card_profiles_days", "payment_day BETWEEN 1 AND 31 AND (closing_day IS NULL OR closing_day BETWEEN 1 AND 31)");
                    table.CheckConstraint("ck_credit_card_profiles_limit", "credit_limit > 0");
                    table.ForeignKey(
                        name: "fk_credit_card_profiles_accounts_id",
                        column: x => x.account_id,
                        principalSchema: "ledger",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.CheckConstraint("ck_exchange_rates_rate", "rate > 0 AND currency <> base_currency");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts",
                sql: "type IN ('Bank', 'Cash', 'Wallet', 'CreditCard')");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_owner_currency_as_of",
                schema: "ledger",
                table: "exchange_rates",
                columns: new[] { "workspace_id", "owner_user_id", "base_currency", "currency", "as_of" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_card_profiles",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "ledger");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "include_in_net_worth",
                schema: "ledger",
                table: "accounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts",
                sql: "type IN ('Bank', 'Cash', 'Wallet')");
        }
    }
}
