using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_personal",
                schema: "workspaces",
                table: "workspaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Written by hand. Until now nothing told the workspace a person got with their account from the ones
            // they created later: the oldest one each person owns is taken as their personal one.
            migrationBuilder.Sql("""
                UPDATE workspaces.workspaces AS workspace
                SET is_personal = TRUE
                WHERE workspace.id = (
                    SELECT oldest.id
                    FROM workspaces.workspaces AS oldest
                    WHERE oldest.owner_user_id = workspace.owner_user_id
                    ORDER BY oldest.created_at, oldest.id
                    LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_one_personal_per_owner",
                schema: "workspaces",
                table: "workspaces",
                column: "owner_user_id",
                unique: true,
                filter: "is_personal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workspaces_one_personal_per_owner",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "is_personal",
                schema: "workspaces",
                table: "workspaces");
        }
    }
}
