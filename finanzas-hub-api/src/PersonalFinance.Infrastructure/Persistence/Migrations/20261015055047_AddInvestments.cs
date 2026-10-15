using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts");

            migrationBuilder.EnsureSchema(
                name: "investments");

            migrationBuilder.CreateTable(
                name: "holdings",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ticker = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    institution = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    term_annual_rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    term_days = table.Column<int>(type: "integer", nullable: true),
                    term_matures_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holdings", x => x.id);
                    table.CheckConstraint("ck_holdings_type", "type IN ('MutualFund', 'TermDeposit', 'Stock', 'Etf', 'Crypto', 'Other')");
                });

            migrationBuilder.CreateTable(
                name: "holding_movements",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    capital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    cash_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holding_movements", x => x.id);
                    table.CheckConstraint("ck_holding_movements_amount", "amount > 0 AND capital >= 0");
                    table.CheckConstraint("ck_holding_movements_kind", "kind IN ('Contribution', 'Withdrawal')");
                    table.ForeignKey(
                        name: "fk_holding_movements_holdings_holding_id",
                        column: x => x.holding_id,
                        principalSchema: "investments",
                        principalTable: "holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "holding_valuations",
                schema: "investments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holding_valuations", x => x.id);
                    table.CheckConstraint("ck_holding_valuations_value", "value >= 0");
                    table.ForeignKey(
                        name: "fk_holding_valuations_holdings_holding_id",
                        column: x => x.holding_id,
                        principalSchema: "investments",
                        principalTable: "holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts",
                sql: "type IN ('Bank', 'Cash', 'Wallet', 'CreditCard', 'GoalFund', 'Investment')");

            migrationBuilder.CreateIndex(
                name: "ix_holding_movements_holding_id",
                schema: "investments",
                table: "holding_movements",
                column: "holding_id");

            migrationBuilder.CreateIndex(
                name: "ix_holding_valuations_holding_id",
                schema: "investments",
                table: "holding_valuations",
                column: "holding_id");

            migrationBuilder.CreateIndex(
                name: "ix_holdings_account_id",
                schema: "investments",
                table: "holdings",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holdings_workspace_id_owner_user_id_closed_on",
                schema: "investments",
                table: "holdings",
                columns: new[] { "workspace_id", "owner_user_id", "closed_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holding_movements",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "holding_valuations",
                schema: "investments");

            migrationBuilder.DropTable(
                name: "holdings",
                schema: "investments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts",
                sql: "type IN ('Bank', 'Cash', 'Wallet', 'CreditCard', 'GoalFund')");
        }
    }
}
