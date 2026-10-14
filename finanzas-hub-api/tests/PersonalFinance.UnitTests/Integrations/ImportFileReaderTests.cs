using PersonalFinance.Modules.Integrations.Domain;

namespace PersonalFinance.UnitTests.Integrations;

public class ImportFileReaderTests
{
    [Fact]
    public void A_Bluecoins_export_keeps_its_accounts_and_categories_and_joins_the_two_lines_of_a_transfer()
    {
        const string File = """"
            (1)Type,(2)Date,(3)Item or Payee,(4)Amount,(5)Currency,(6)ConversionRate,(7)Parent Category,(8)Category,(9)Account Type,(10)Account,(11)Notes,(12) Label,(13) Status
            New Account,2026-09-01 08:00,Efectivo,200.00,PEN,1,,(New Account),Cash,Efectivo,,,
            e,2026-10-01 13:20,"Almuerzo, con ""el equipo""",-42.50,PEN,1,Comida,Restaurantes,Cash,Efectivo,una nota,"trabajo, viaje largo",
            Income,2026-10-02,Sueldo,"3,500.00",PEN,1,Ingresos,Sueldo,Bank,BCP,,,
            Transfer,2026-10-03 10:00,Retiro,-300,PEN,1,(Transfer),(Transfer),Bank,BCP,,,
            Transfer,2026-10-03 10:00,Retiro,300,PEN,1,(Transfer),(Transfer),Cash,Efectivo,,,
            Expense,sin fecha,Algo,-5,PEN,1,Comida,Restaurantes,Cash,Efectivo,,,
            Transfer,2026-10-05,Suelta,-10,PEN,1,,,Bank,BCP,,,
            """";

        var parsed = ImportFileReader.Read(File).Value;

        Assert.Equal(ImportFormat.Bluecoins, parsed.Format);
        Assert.Equal(
            [(ImportKind.OpeningBalance, 200m), (ImportKind.Expense, 42.50m), (ImportKind.Income, 3500m), (ImportKind.Transfer, 300m), (ImportKind.Expense, 5m), (ImportKind.Transfer, 10m)],
            parsed.Rows.Select(row => (row.Kind, row.Amount)));

        var lunch = parsed.Rows[1];
        Assert.Equal(("Almuerzo, con \"el equipo\"", "Efectivo", "Comida", "Restaurantes", "una nota"), (lunch.Description, lunch.Account, lunch.CategoryGroup, lunch.Category, lunch.Note));
        Assert.Equal((new DateOnly(2026, 10, 1), "PEN"), (lunch.OccurredOn, lunch.Currency));
        Assert.Equal(["trabajo", "viaje-largo"], lunch.Labels);

        // The two lines of the withdrawal are one transfer, from the account the money left.
        var transfer = parsed.Rows[3];
        Assert.Equal(("BCP", "Efectivo", null, null), (transfer.Account, transfer.CounterAccount, transfer.CounterAmount, transfer.Error));

        // What cannot be read stays, with its reason.
        Assert.Equal(["Fecha no reconocida.", "Transferencia sin su otra mitad en el archivo."], parsed.Rows.Skip(4).Select(row => row.Error));
    }

    [Fact]
    public void A_bank_table_is_read_whatever_its_separator_its_dates_and_the_way_it_writes_amounts()
    {
        const string File = "Fecha;Descripción;Cargo;Abono\n05/10/2026;TIENDA XYZ;25,00;\n13/10/2026;ABONO HABERES;;1.500,00\n14/10/2026;SIN MONTO;;\n";

        var parsed = ImportFileReader.Read(File).Value;

        Assert.Equal(ImportFormat.Csv, parsed.Format);
        Assert.Equal(
            [(ImportKind.Expense, 25m, (DateOnly?)new DateOnly(2026, 10, 5), null), (ImportKind.Income, 1500m, new DateOnly(2026, 10, 13), null), (ImportKind.Expense, 0m, new DateOnly(2026, 10, 14), "Monto vacío o en cero.")],
            parsed.Rows.Select(row => (row.Kind, row.Amount, row.OccurredOn, row.Error)));

        // One date that only reads month first turns the whole file to month first.
        var american = ImportFileReader.Read("Date,Description,Amount\n10/05/2026,Coffee,-4.50\n10/13/2026,Refund,4.50\n").Value;
        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 13)], american.Rows.Select(row => row.OccurredOn!.Value));
        Assert.Equal([ImportKind.Expense, ImportKind.Income], american.Rows.Select(row => row.Kind));

        Assert.Equal("imports.format_not_recognized", ImportFileReader.Read("Nombre,Edad\nBrian,30\n").Error.Code);
        Assert.Equal(
            [1234.56m, 1234.56m, 1234m, 45.9m, -12m, -12.5m, 0.5m],
            new[] { "1,234.56", "S/ 1.234,56", "1,234", "45.90", "(12.00)", "-12,5", ".50" }.Select(text => ImportFileReader.ParseAmount(text)!.Value));
    }

    [Fact]
    public void An_OFX_statement_gives_one_line_per_transaction_with_the_sign_as_the_kind()
    {
        const string File = """
            OFXHEADER:100
            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>PEN
            <BANKTRANLIST>
            <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261005120000<TRNAMT>-25.00<FITID>1<NAME>TIENDA XYZ<MEMO>Compra con tarjeta
            <STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20261006<TRNAMT>1500.00<FITID>2<NAME>ABONO HABERES</STMTTRN>
            </BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """;

        var parsed = ImportFileReader.Read(File).Value;

        Assert.Equal(ImportFormat.Ofx, parsed.Format);
        Assert.Equal(
            [(ImportKind.Expense, 25m, "TIENDA XYZ", "Compra con tarjeta", "PEN"), (ImportKind.Income, 1500m, "ABONO HABERES", null, "PEN")],
            parsed.Rows.Select(row => (row.Kind, row.Amount, row.Description, row.Note, row.Currency)));
        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6)], parsed.Rows.Select(row => row.OccurredOn!.Value));
    }
}
