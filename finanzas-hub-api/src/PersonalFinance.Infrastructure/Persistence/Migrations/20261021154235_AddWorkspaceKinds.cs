using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every workspace that exists is either someone's personal one or one created to share: both are of kind
            // Personal (ADR-023). Nothing moves.
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Personal");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspaces_kind",
                schema: "workspaces",
                table: "workspaces",
                sql: "kind IN ('Personal', 'Business', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspaces_personal_kind",
                schema: "workspaces",
                table: "workspaces",
                sql: "NOT is_personal OR kind = 'Personal'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_workspaces_kind",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workspaces_personal_kind",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "workspaces",
                table: "workspaces");
        }
    }
}
