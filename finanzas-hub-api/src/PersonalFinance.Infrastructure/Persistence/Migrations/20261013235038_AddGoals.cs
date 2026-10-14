using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts");

            migrationBuilder.EnsureSchema(
                name: "goals");

            migrationBuilder.CreateTable(
                name: "goals",
                schema: "goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    achieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goals", x => x.id);
                    table.CheckConstraint("ck_goals_status", "status IN ('Active', 'Archived', 'Cancelled')");
                    table.CheckConstraint("ck_goals_target_amount", "target_amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "goal_contributions",
                schema: "goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ledger_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_contributions", x => x.id);
                    table.CheckConstraint("ck_goal_contributions_amount", "amount > 0");
                    table.CheckConstraint("ck_goal_contributions_type", "type IN ('Contribution', 'Withdrawal')");
                    table.ForeignKey(
                        name: "fk_goal_contributions_goals_goal_id",
                        column: x => x.goal_id,
                        principalSchema: "goals",
                        principalTable: "goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goal_participants",
                schema: "goals",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fund_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_participants", x => new { x.goal_id, x.user_id });
                    table.CheckConstraint("ck_goal_participants_role", "role IN ('Owner', 'Participant')");
                    table.CheckConstraint("ck_goal_participants_status", "status IN ('Active', 'Left')");
                    table.ForeignKey(
                        name: "fk_goal_participants_goals_goal_id",
                        column: x => x.goal_id,
                        principalSchema: "goals",
                        principalTable: "goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts",
                sql: "type IN ('Bank', 'Cash', 'Wallet', 'CreditCard', 'GoalFund')");

            migrationBuilder.CreateIndex(
                name: "ix_goal_contributions_goal_id_occurred_on",
                schema: "goals",
                table: "goal_contributions",
                columns: new[] { "goal_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_contributions_ledger_operation_id",
                schema: "goals",
                table: "goal_contributions",
                column: "ledger_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goal_participants_one_owner",
                schema: "goals",
                table: "goal_participants",
                column: "goal_id",
                unique: true,
                filter: "role = 'Owner' AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_goal_participants_user_id_status",
                schema: "goals",
                table: "goal_participants",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_goals_workspace_id_status",
                schema: "goals",
                table: "goals",
                columns: new[] { "workspace_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goal_contributions",
                schema: "goals");

            migrationBuilder.DropTable(
                name: "goal_participants",
                schema: "goals");

            migrationBuilder.DropTable(
                name: "goals",
                schema: "goals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_type",
                schema: "ledger",
                table: "accounts",
                sql: "type IN ('Bank', 'Cash', 'Wallet', 'CreditCard')");
        }
    }
}
