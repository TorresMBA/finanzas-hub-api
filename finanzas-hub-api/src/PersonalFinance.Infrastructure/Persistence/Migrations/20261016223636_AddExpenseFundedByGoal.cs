using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseFundedByGoal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "funded_by_goal_id",
                schema: "ledger",
                table: "operations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "funded_by_goal_id",
                schema: "ledger",
                table: "operations");
        }
    }
}
