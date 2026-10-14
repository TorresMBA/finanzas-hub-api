using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shared_expenses");

            migrationBuilder.CreateTable(
                name: "shared_groups",
                schema: "shared_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_groups", x => x.id);
                    table.CheckConstraint("ck_shared_groups_status", "status IN ('Active', 'Archived')");
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                schema: "shared_expenses",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_members", x => new { x.group_id, x.user_id });
                    table.CheckConstraint("ck_group_members_role", "role IN ('Admin', 'Member')");
                    table.CheckConstraint("ck_group_members_status", "status IN ('Active', 'Left')");
                    table.ForeignKey(
                        name: "fk_group_members_shared_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "shared_expenses",
                        principalTable: "shared_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "settlements",
                schema: "shared_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    from_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlements", x => x.id);
                    table.CheckConstraint("ck_settlements_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_settlements_shared_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "shared_expenses",
                        principalTable: "shared_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shared_expenses",
                schema: "shared_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    split_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payer_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payer_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_expenses", x => x.id);
                    table.CheckConstraint("ck_shared_expenses_split_type", "split_type IN ('Equal', 'Percentage', 'FixedAmount', 'Shares')");
                    table.CheckConstraint("ck_shared_expenses_total", "total_amount > 0");
                    table.ForeignKey(
                        name: "fk_shared_expenses_shared_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "shared_expenses",
                        principalTable: "shared_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "settlement_allocations",
                schema: "shared_expenses",
                columns: table => new
                {
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_allocations", x => new { x.settlement_id, x.expense_id });
                    table.ForeignKey(
                        name: "fk_settlement_allocations_settlements_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "shared_expenses",
                        principalTable: "settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_shares",
                schema: "shared_expenses",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_shares", x => new { x.expense_id, x.user_id });
                    table.CheckConstraint("ck_expense_shares_amounts", "amount >= 0 AND paid_amount >= 0 AND paid_amount <= amount");
                    table.ForeignKey(
                        name: "fk_expense_shares_shared_expenses_expense_id",
                        column: x => x.expense_id,
                        principalSchema: "shared_expenses",
                        principalTable: "shared_expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_members_user_id_status",
                schema: "shared_expenses",
                table: "group_members",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_settlements_group_id_occurred_on",
                schema: "shared_expenses",
                table: "settlements",
                columns: new[] { "group_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_expenses_group_id_occurred_on",
                schema: "shared_expenses",
                table: "shared_expenses",
                columns: new[] { "group_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_groups_workspace_id_status",
                schema: "shared_expenses",
                table: "shared_groups",
                columns: new[] { "workspace_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_shares",
                schema: "shared_expenses");

            migrationBuilder.DropTable(
                name: "group_members",
                schema: "shared_expenses");

            migrationBuilder.DropTable(
                name: "settlement_allocations",
                schema: "shared_expenses");

            migrationBuilder.DropTable(
                name: "shared_expenses",
                schema: "shared_expenses");

            migrationBuilder.DropTable(
                name: "settlements",
                schema: "shared_expenses");

            migrationBuilder.DropTable(
                name: "shared_groups",
                schema: "shared_expenses");
        }
    }
}
