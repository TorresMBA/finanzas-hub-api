using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "recurring_rules",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    posting_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reminder_days_before = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pause_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_rules", x => x.id);
                    table.CheckConstraint("ck_recurring_rules_amount", "amount > 0");
                    table.CheckConstraint("ck_recurring_rules_frequency", "frequency IN ('Once', 'Monthly', 'Yearly')");
                    table.CheckConstraint("ck_recurring_rules_period", "ends_on IS NULL OR ends_on >= starts_on");
                    table.CheckConstraint("ck_recurring_rules_posting_mode", "posting_mode IN ('Automatic', 'Confirm')");
                    table.CheckConstraint("ck_recurring_rules_status", "status IN ('Active', 'Paused', 'Ended')");
                    table.CheckConstraint("ck_recurring_rules_type", "type IN ('Income', 'Expense')");
                });

            migrationBuilder.CreateTable(
                name: "occurrences",
                schema: "scheduling",
                columns: table => new
                {
                    scheduled_on = table.Column<DateOnly>(type: "date", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occurrences", x => new { x.rule_id, x.scheduled_on });
                    table.ForeignKey(
                        name: "fk_occurrences_recurring_rules_rule_id",
                        column: x => x.rule_id,
                        principalSchema: "scheduling",
                        principalTable: "recurring_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_rules_workspace_id_owner_user_id_status",
                schema: "scheduling",
                table: "recurring_rules",
                columns: new[] { "workspace_id", "owner_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "occurrences",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "recurring_rules",
                schema: "scheduling");
        }
    }
}
