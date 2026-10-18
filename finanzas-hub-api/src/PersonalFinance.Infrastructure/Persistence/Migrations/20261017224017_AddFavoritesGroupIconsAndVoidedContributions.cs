using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoritesGroupIconsAndVoidedContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "icon",
                schema: "shared_expenses",
                table: "shared_groups",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "group");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                schema: "goals",
                table: "goal_contributions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_favorite",
                schema: "ledger",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Who owns no personal workspace (they handed over the only one they had before ADR-021) gets one, so
            // they see their finances again. It takes the currency and time zone of the oldest workspace they are
            // active in; without one it waits for its onboarding.
            migrationBuilder.Sql("""
                WITH missing AS (
                    SELECT person.id AS user_id, source.base_currency, source.time_zone
                    FROM identity.users AS person
                    LEFT JOIN LATERAL (
                        SELECT workspace.base_currency, workspace.time_zone
                        FROM workspaces.memberships AS membership
                        JOIN workspaces.workspaces AS workspace ON workspace.id = membership.workspace_id
                        WHERE membership.user_id = person.id AND membership.status = 'Active' AND workspace.status = 'Active'
                        ORDER BY membership.joined_at
                        LIMIT 1) AS source ON TRUE
                    WHERE NOT EXISTS (
                        SELECT 1 FROM workspaces.workspaces AS personal WHERE personal.owner_user_id = person.id AND personal.is_personal)),
                created AS (
                    INSERT INTO workspaces.workspaces (id, base_currency, created_at, is_personal, name, owner_user_id, status, time_zone)
                    SELECT gen_random_uuid(), base_currency, now(), TRUE, 'Personal', user_id,
                           CASE WHEN base_currency IS NULL THEN 'PendingSetup' ELSE 'Active' END, COALESCE(time_zone, 'America/Lima')
                    FROM missing
                    RETURNING id, owner_user_id)
                INSERT INTO workspaces.memberships (id, joined_at, role, status, user_id, workspace_id)
                SELECT gen_random_uuid(), now(), 'Owner', 'Active', owner_user_id, id
                FROM created;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "icon",
                schema: "shared_expenses",
                table: "shared_groups");

            migrationBuilder.DropColumn(
                name: "voided_at",
                schema: "goals",
                table: "goal_contributions");

            migrationBuilder.DropColumn(
                name: "is_favorite",
                schema: "ledger",
                table: "accounts");
        }
    }
}
