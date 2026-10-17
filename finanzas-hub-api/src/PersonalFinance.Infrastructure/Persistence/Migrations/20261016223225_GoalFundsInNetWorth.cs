using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalFinance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The fund of a goal counts in the net worth from now on (ADR-013, update of 16-oct-2026): the funds already
    /// open are included too. Whoever wants one out unticks it in Cuentas, as with any account.
    /// </summary>
    public partial class GoalFundsInNetWorth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE ledger.accounts SET include_in_net_worth = true WHERE type = 'GoalFund';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Which funds were out before is not kept, so there is nothing to put back.
        }
    }
}
