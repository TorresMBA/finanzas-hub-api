using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    institution = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_type", "type IN ('Bank', 'Cash', 'Wallet')");
                });

            migrationBuilder.CreateTable(
                name: "operations",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    merchant = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_module = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                    table.CheckConstraint("ck_operations_category", "kind NOT IN ('Income', 'Expense', 'Refund') OR category_id IS NOT NULL");
                    table.CheckConstraint("ck_operations_kind", "kind IN ('Income', 'Expense', 'Refund', 'Transfer', 'Adjustment', 'OpeningBalance')");
                    table.CheckConstraint("ck_operations_status", "status IN ('Pending', 'Confirmed', 'Voided')");
                });

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false, computedColumnSql: "CASE WHEN direction = 'In' THEN amount ELSE -amount END", stored: true),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_effective = table.Column<bool>(type: "boolean", nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    fx_rate_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                    table.CheckConstraint("ck_entries_amount", "amount > 0");
                    table.CheckConstraint("ck_entries_direction", "direction IN ('In', 'Out')");
                    table.ForeignKey(
                        name: "fk_entries_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "ledger",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entries_operations_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "ledger",
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operation_tags",
                schema: "ledger",
                columns: table => new
                {
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_tags", x => new { x.operation_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_operation_tags_operations_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "ledger",
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_workspace_id_owner_user_id_name_key",
                schema: "ledger",
                table: "accounts",
                columns: new[] { "workspace_id", "owner_user_id", "name_key" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entries_account_id_occurred_on",
                schema: "ledger",
                table: "entries",
                columns: new[] { "account_id", "occurred_on" },
                filter: "is_effective")
                .Annotation("Npgsql:IndexInclude", new[] { "signed_amount" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_operation_id",
                schema: "ledger",
                table: "entries",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_tags_tag_id",
                schema: "ledger",
                table: "operation_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_owner_user_id_category_id_occurred_on",
                schema: "ledger",
                table: "operations",
                columns: new[] { "owner_user_id", "category_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_operations_source_module_source_type_source_id",
                schema: "ledger",
                table: "operations",
                columns: new[] { "source_module", "source_type", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_operations_workspace_id_owner_user_id_occurred_on_id",
                schema: "ledger",
                table: "operations",
                columns: new[] { "workspace_id", "owner_user_id", "occurred_on", "id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entries",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "operation_tags",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "ledger");
        }
    }
}
