using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "categories");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    system_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.CheckConstraint("ck_categories_color", "color IN ('Green', 'Blue', 'Violet', 'Amber', 'Red')");
                    table.CheckConstraint("ck_categories_kind", "kind IN ('Income', 'Expense')");
                    table.CheckConstraint("ck_categories_look", "(parent_id IS NULL) = (icon IS NOT NULL) AND (parent_id IS NULL) = (color IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "categories",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category_tags",
                schema: "categories",
                columns: table => new
                {
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_tags", x => new { x.category_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_category_tags_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "categories",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_category_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "categories",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id",
                schema: "categories",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_workspace_id_owner_user_id_parent_id_name_key",
                schema: "categories",
                table: "categories",
                columns: new[] { "workspace_id", "owner_user_id", "parent_id", "name_key" },
                unique: true,
                filter: "archived_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_categories_workspace_id_owner_user_id_system_key",
                schema: "categories",
                table: "categories",
                columns: new[] { "workspace_id", "owner_user_id", "system_key" },
                unique: true,
                filter: "system_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_category_tags_tag_id",
                schema: "categories",
                table: "category_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_workspace_id_owner_user_id_name",
                schema: "categories",
                table: "tags",
                columns: new[] { "workspace_id", "owner_user_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "category_tags",
                schema: "categories");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "categories");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "categories");
        }
    }
}
