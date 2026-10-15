using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowXlsxImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_import_sessions_format",
                schema: "integrations",
                table: "import_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_import_sessions_format",
                schema: "integrations",
                table: "import_sessions",
                sql: "format IN ('Csv', 'Bluecoins', 'Ofx', 'Xlsx')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_import_sessions_format",
                schema: "integrations",
                table: "import_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_import_sessions_format",
                schema: "integrations",
                table: "import_sessions",
                sql: "format IN ('Csv', 'Bluecoins', 'Ofx')");
        }
    }
}
