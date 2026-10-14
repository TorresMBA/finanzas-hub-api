using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "categorization_rules",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pattern = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    pattern_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categorization_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_sessions",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    file_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    record_count = table.Column<int>(type: "integer", nullable: false),
                    imported_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_sessions", x => x.id);
                    table.CheckConstraint("ck_import_sessions_format", "format IN ('Csv', 'Bluecoins', 'Ofx')");
                    table.CheckConstraint("ck_import_sessions_status", "status IN ('Review', 'Committed')");
                });

            migrationBuilder.CreateTable(
                name: "import_accounts",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    create_as = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_accounts_import_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "integrations",
                        principalTable: "import_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_categories",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_income = table.Column<bool>(type: "boolean", nullable: false),
                    source_group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    create = table.Column<bool>(type: "boolean", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_categories_import_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "integrations",
                        principalTable: "import_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_records",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    counter_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_account = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_counter_account = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_category_group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    labels = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_duplicate = table.Column<bool>(type: "boolean", nullable: false),
                    excluded = table.Column<bool>(type: "boolean", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_records", x => x.id);
                    table.CheckConstraint("ck_import_records_kind", "kind IN ('Income', 'Expense', 'Transfer', 'OpeningBalance')");
                    table.ForeignKey(
                        name: "fk_import_records_import_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "integrations",
                        principalTable: "import_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categorization_rules_owner_pattern",
                schema: "integrations",
                table: "categorization_rules",
                columns: new[] { "workspace_id", "owner_user_id", "pattern_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_accounts_session_id",
                schema: "integrations",
                table: "import_accounts",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_categories_session_id",
                schema: "integrations",
                table: "import_categories",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_records_session_id_line",
                schema: "integrations",
                table: "import_records",
                columns: new[] { "session_id", "line" });

            migrationBuilder.CreateIndex(
                name: "ix_import_sessions_workspace_id_owner_user_id_created_at",
                schema: "integrations",
                table: "import_sessions",
                columns: new[] { "workspace_id", "owner_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categorization_rules",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "import_accounts",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "import_categories",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "import_records",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "import_sessions",
                schema: "integrations");
        }
    }
}
