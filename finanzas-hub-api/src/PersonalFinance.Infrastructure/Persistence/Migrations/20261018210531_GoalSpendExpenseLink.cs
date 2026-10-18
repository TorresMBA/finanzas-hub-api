using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoalSpendExpenseLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "expense_operation_id",
                schema: "goals",
                table: "goal_contributions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_goal_contributions_expense_operation_id",
                schema: "goals",
                table: "goal_contributions",
                column: "expense_operation_id",
                unique: true,
                filter: "expense_operation_id IS NOT NULL");

            // The spends registered before this column: the audit trail of each one says which expense it paid.
            migrationBuilder.Sql("""
                UPDATE goals.goal_contributions AS contribution
                SET expense_operation_id = (audit.after ->> 'expenseId')::uuid
                FROM platform.audit_log AS audit
                WHERE audit.action = 'goals.contribution.spent'
                  AND (audit.after ->> 'contributionId')::uuid = contribution.id
                  AND audit.after ->> 'expenseId' IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_goal_contributions_expense_operation_id",
                schema: "goals",
                table: "goal_contributions");

            migrationBuilder.DropColumn(
                name: "expense_operation_id",
                schema: "goals",
                table: "goal_contributions");
        }
    }
}
