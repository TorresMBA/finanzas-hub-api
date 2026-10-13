using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "budgets");

            migrationBuilder.CreateTable(
                name: "budgets",
                schema: "budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    alert_threshold_percent = table.Column<int>(type: "integer", nullable: false),
                    is_fixed = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budgets", x => x.id);
                    table.CheckConstraint("ck_budgets_alert_threshold", "alert_threshold_percent IN (75, 90, 100)");
                    table.CheckConstraint("ck_budgets_amount", "amount > 0");
                    table.CheckConstraint("ck_budgets_validity", "ends_on IS NULL OR ends_on >= effective_from");
                });

            migrationBuilder.CreateIndex(
                name: "ix_budgets_owner_category_effective_from",
                schema: "budgets",
                table: "budgets",
                columns: new[] { "workspace_id", "owner_user_id", "category_id", "effective_from" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budgets",
                schema: "budgets");
        }
    }
}
