using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Integrations.Domain;

public enum ImportFormat
{
    /// <summary>A statement from a bank as a table: a date, a description and an amount (or a charge and a deposit).</summary>
    Csv,

    /// <summary>The export of the Bluecoins mobile app: it brings its own accounts, categories and transfers.</summary>
    Bluecoins,

    /// <summary>The statement format most banks offer to download.</summary>
    Ofx,

    /// <summary>A bank statement as an Excel workbook: its first sheet is read like a CSV table.</summary>
    Xlsx
}

public enum ImportKind
{
    Income,
    Expense,
    Transfer,

    /// <summary>The balance an account started with in the application the file comes from.</summary>
    OpeningBalance
}

/// <summary>One movement as the file states it, before deciding where it goes.</summary>
/// <param name="Line">Where it is in the file, counting the header as line 1.</param>
/// <param name="Amount">Positive. For an opening balance it carries its sign.</param>
/// <param name="CounterAmount">What arrives in the destination of a transfer when it is not what left.</param>
/// <param name="Account">The name of the account in the file; empty when the file is of a single account.</param>
/// <param name="Error">Why the line cannot be imported. The other fields are then whatever could be read.</param>
public sealed record ParsedRow(
    int Line, ImportKind Kind, DateOnly? OccurredOn, decimal Amount, decimal? CounterAmount, string? Currency, string Description, string? Note, string Account,
    string? CounterAccount, string? CategoryGroup, string Category, IReadOnlyList<string> Labels, string? Error);

public sealed record ParsedFile(ImportFormat Format, IReadOnlyList<ParsedRow> Rows);

/// <summary>
/// Reads a file of movements. It recognises by itself an OFX statement, the export of Bluecoins and a table from
/// a bank. A line it cannot make sense of stays in the result with its reason, so the person sees what was left out.
/// </summary>
public static partial class ImportFileReader
{
    public static readonly Error NotRecognized = Error.Validation(
        "imports.format_not_recognized",
        "The file is not an OFX statement, a Bluecoins export or a table with a date, a description and an amount.");

    public static Result<ParsedFile> Read(string text)
    {
        if (text.Contains("<OFX", StringComparison.OrdinalIgnoreCase) || text.Contains("OFXHEADER", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedFile(ImportFormat.Ofx, ReadOfx(text));
        }

        var firstLine = text.AsSpan().TrimStart().ToString().Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t', '|' }.MaxBy(candidate => firstLine.Count(character => character == candidate));
        var table = Table(text, delimiter);
        if (table.Count < 2)
        {
            return NotRecognized;
        }

        var header = table[0].Select(HeaderKey).ToList();
        int Column(params string[] names) => header.FindIndex(name => names.Contains(name));

        var bluecoins = (Type: Column("type", "tipo"), Date: Column("date", "fecha"), Title: Column("title", "item or payee", "titulo"), Amount: Column("amount", "monto", "importe"),
            Account: Column("account", "cuenta"));
        if (bluecoins is { Type: >= 0, Date: >= 0, Title: >= 0, Amount: >= 0, Account: >= 0 })
        {
            return new ParsedFile(ImportFormat.Bluecoins, ReadBluecoins(table, Column));
        }

        var rows = ReadTable(table, Column);
        return rows is null ? NotRecognized : new ParsedFile(ImportFormat.Csv, rows);
    }

    // --- Bluecoins.

    private static List<ParsedRow> ReadBluecoins(List<List<string>> table, Func<string[], int> column)
    {
        var (type, date, title, amount, account) = (column(["type", "tipo"]), column(["date", "fecha"]), column(["title", "item or payee", "titulo"]),
            column(["amount", "monto", "importe"]), column(["account", "cuenta"]));
        var (currency, group, category, notes, labels) = (column(["currency", "moneda"]), column(["category group name", "parent category", "grupo de categoria"]),
            column(["category", "categoria"]), column(["notes", "notas"]), column(["labels", "label", "etiquetas"]));
        var dayFirst = DayFirst(table.Skip(1).Select(cells => Cell(cells, date)));

        var rows = new List<ParsedRow>();
        ParsedRow Row(int line, List<string> cells, ImportKind kind, decimal value, string? error, string? counterAccount = null, decimal? counterAmount = null)
        {
            var isTransaction = kind is ImportKind.Income or ImportKind.Expense;
            var day = ParseDate(Cell(cells, date), dayFirst);
            return new ParsedRow(
                line, kind, day, value, counterAmount, CurrencyOf(Cell(cells, currency)),
                Cut(Cell(cells, title), 120) ?? (kind == ImportKind.Transfer ? "Transferencia" : "Movimiento importado"), Cut(Cell(cells, notes), 500), Cell(cells, account),
                counterAccount, isTransaction ? Cell(cells, group) : null, isTransaction ? Cell(cells, category) : string.Empty, LabelsOf(Cell(cells, labels)),
                error ?? (day is null ? "Fecha no reconocida." : Cell(cells, account).Length == 0 ? "Falta la cuenta." : null));
        }

        (int Line, List<string> Cells, decimal Amount)? half = null;
        foreach (var (index, cells) in table.Index().Skip(1))
        {
            var line = index + 1;
            var kind = KindOf(Cell(cells, type));
            var value = ParseAmount(Cell(cells, amount));
            if (kind is null || value is null or 0)
            {
                rows.Add(Row(line, cells, kind ?? ImportKind.Expense, Math.Abs(value ?? 0), kind is null ? "Tipo de movimiento no reconocido." : "Monto vacío o en cero."));
            }
            else if (kind != ImportKind.Transfer)
            {
                rows.Add(Row(line, cells, kind.Value, kind == ImportKind.OpeningBalance ? value.Value : Math.Abs(value.Value), null));
            }
            else if (half is not { } first)
            {
                // A transfer comes as two lines, one per account: what leaves is negative, what arrives positive.
                half = (line, cells, value.Value);
            }
            else
            {
                half = null;
                var second = (Line: line, Cells: cells, Amount: value.Value);
                var (leaving, arriving) = first.Amount > 0 && second.Amount < 0 ? (second, first) : (first, second);
                var (amountOut, amountIn) = (Math.Abs(leaving.Amount), Math.Abs(arriving.Amount));
                var to = Cell(arriving.Cells, account);
                rows.Add(Row(
                    first.Line, leaving.Cells, ImportKind.Transfer, amountOut,
                    to.Length == 0 ? "Falta una de las dos cuentas de la transferencia."
                        : string.Equals(Cell(leaving.Cells, account), to, StringComparison.OrdinalIgnoreCase) ? "La transferencia sale y llega a la misma cuenta."
                        : null,
                    to, amountIn == amountOut ? null : amountIn));
            }
        }

        if (half is { } alone)
        {
            rows.Add(Row(alone.Line, alone.Cells, ImportKind.Transfer, Math.Abs(alone.Amount), "Transferencia sin su otra mitad en el archivo."));
        }

        return rows.OrderBy(row => row.Line).ToList();
    }

    private static ImportKind? KindOf(string type) => TextKey.Of(type) switch
    {
        "e" or "expense" or "gasto" => ImportKind.Expense,
        "i" or "income" or "ingreso" => ImportKind.Income,
        "t" or "transfer" or "transferencia" => ImportKind.Transfer,
        "n" or "new account" or "nueva cuenta" or "cuenta nueva" => ImportKind.OpeningBalance,
        _ => null
    };

    private static List<string> LabelsOf(string labels) => labels
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(label => new string(label.Replace(' ', '-').Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray()))
        .Where(label => label.Length is > 0 and <= 40)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(10)
        .ToList();

    // --- A table from a bank.

    private static List<ParsedRow>? ReadTable(List<List<string>> table, Func<string[], int> column)
    {
        var date = column(["fecha", "date", "fecha operacion", "fecha de operacion", "fecha movimiento", "fecha de movimiento", "fecha proceso", "fecha valor"]);
        var description = column(["descripcion", "description", "concepto", "detalle", "glosa", "movimiento", "referencia", "memo", "payee"]);
        var amount = column(["monto", "importe", "amount", "valor"]);
        var charge = column(["cargo", "cargos", "debito", "debe", "debit", "retiro", "retiros", "egreso"]);
        var deposit = column(["abono", "abonos", "credito", "haber", "credit", "deposito", "depositos", "ingreso"]);
        if (date < 0 || (amount < 0 && charge < 0 && deposit < 0))
        {
            return null;
        }

        var (category, currency) = (column(["categoria", "category"]), column(["moneda", "currency", "divisa"]));
        var dayFirst = DayFirst(table.Skip(1).Select(cells => Cell(cells, date)));
        return table.Index().Skip(1).Select(row =>
        {
            var cells = row.Item;
            var signed = amount >= 0 ? ParseAmount(Cell(cells, amount)) : (ParseAmount(Cell(cells, deposit)) ?? 0) - Math.Abs(ParseAmount(Cell(cells, charge)) ?? 0);
            var day = ParseDate(Cell(cells, date), dayFirst);
            return new ParsedRow(
                row.Index + 1, signed > 0 ? ImportKind.Income : ImportKind.Expense, day, Math.Abs(signed ?? 0), null, CurrencyOf(Cell(cells, currency)),
                Cut(Cell(cells, description), 120) ?? "Movimiento importado", null, string.Empty, null, null, Cell(cells, category), [],
                day is null ? "Fecha no reconocida." : signed is null or 0 ? "Monto vacío o en cero." : null);
        }).ToList();
    }

    // --- OFX.

    [GeneratedRegex(@"<STMTTRN>(.*?)(?=<STMTTRN>|</STMTTRN>|</BANKTRANLIST>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex OfxTransaction();

    private static List<ParsedRow> ReadOfx(string text)
    {
        static string Tag(string block, string name) =>
            Regex.Match(block, $@"<{name}>([^<\r\n]*)", RegexOptions.IgnoreCase) is { Success: true } match ? match.Groups[1].Value.Trim() : string.Empty;

        var currency = CurrencyOf(Tag(text, "CURDEF"));
        return OfxTransaction().Matches(text).Index().Select(found =>
        {
            var block = found.Item.Groups[1].Value;
            var signed = ParseAmount(Tag(block, "TRNAMT"));
            var posted = Tag(block, "DTPOSTED");
            DateOnly? day = posted.Length >= 8 && DateOnly.TryParseExact(posted[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
            var (name, memo) = (Tag(block, "NAME"), Tag(block, "MEMO"));
            return new ParsedRow(
                found.Index + 1, signed > 0 ? ImportKind.Income : ImportKind.Expense, day, Math.Abs(signed ?? 0), null, currency,
                Cut(name.Length > 0 ? name : memo, 120) ?? "Movimiento importado", name.Length > 0 ? Cut(memo, 500) : null, string.Empty, null, null, string.Empty, [],
                day is null ? "Fecha no reconocida." : signed is null or 0 ? "Monto vacío o en cero." : null);
        }).ToList();
    }

    // --- Cells.

    /// <summary>Splits a delimited text into rows of cells. A quoted cell may hold the delimiter, a line break or a doubled quote.</summary>
    private static List<List<string>> Table(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        void EndRow()
        {
            cells.Add(cell.ToString().Trim());
            cell.Clear();
            if (cells.Any(value => value.Length > 0))
            {
                rows.Add(cells);
            }

            cells = [];
        }

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == delimiter && !quoted)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else if (character == '\n' && !quoted)
            {
                EndRow();
            }
            else if (character != '\r' && character != '﻿')
            {
                cell.Append(character);
            }
        }

        EndRow();
        return rows;
    }

    private static string Cell(List<string> cells, int column) => column >= 0 && column < cells.Count ? cells[column] : string.Empty;

    [GeneratedRegex(@"^\(\d+\)\s*")]
    private static partial Regex Numbered();

    /// <summary>A header as it is compared: Bluecoins numbers its columns ("(2)Date") and banks write them with accents.</summary>
    private static string HeaderKey(string header) => TextKey.Of(Numbered().Replace(header.Trim(), string.Empty));

    private static string? Cut(string text, int length)
    {
        var clean = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 ? null : clean.Length > length ? clean[..length] : clean;
    }

    private static string? CurrencyOf(string code) => code.Trim().ToUpperInvariant() is { Length: 3 } valid && valid.All(char.IsAsciiLetter) ? valid : null;

    /// <summary>"1,234.56", "1.234,56", "S/ 45.90", "(12.00)" and "-12" are all amounts: the last separator is the decimal one.</summary>
    public static decimal? ParseAmount(string text)
    {
        var negative = text.Contains('-') || (text.Contains('(') && text.Contains(')'));
        var digits = new string(text.Where(character => char.IsAsciiDigit(character) || character is '.' or ',').ToArray()).TrimEnd('.', ',');
        if (digits.Length == 0)
        {
            return null;
        }

        // With both separators the last one is the decimal one. A lone comma before exactly three digits, or a
        // separator that repeats, only groups thousands.
        var last = digits.LastIndexOfAny(['.', ',']);
        var both = digits.Contains('.') && digits.Contains(',');
        var isDecimal = last >= 0 && (both || (digits.Count(character => character == digits[last]) == 1 && !(digits[last] == ',' && digits.Length - last - 1 == 3)));
        var whole = new string((isDecimal ? digits[..last] : digits).Where(char.IsAsciiDigit).ToArray());
        var number = isDecimal ? $"{(whole.Length == 0 ? "0" : whole)}.{digits[(last + 1)..]}" : whole;
        return decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? (negative ? -value : value) : null;
    }

    [GeneratedRegex(@"^\s*(\d{1,4})[/\-.](\d{1,2})[/\-.](\d{1,4})")]
    private static partial Regex DateParts();

    /// <summary>Whether "03/04/2026" is the 3rd of April: yes, unless some date of the file can only be read month first.</summary>
    private static bool DayFirst(IEnumerable<string> dates) => !dates
        .Select(date => DateParts().Match(date))
        .Any(match => match.Success && match.Groups[1].Length <= 2 && int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) <= 12
            && int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) > 12);

    private static DateOnly? ParseDate(string text, bool dayFirst)
    {
        if (DateParts().Match(text) is not { Success: true } match)
        {
            return null;
        }

        var (first, second, third) = (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        var (year, month, day) = match.Groups[1].Length == 4 ? (first, second, third) : dayFirst ? (third, second, first) : (third, first, second);
        year = year < 100 ? 2000 + year : year;
        return year is >= 1990 and <= 2100 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;
    }
}

/// <summary>A text as it is compared: without accents, upper case or repeated spaces.</summary>
public static class TextKey
{
    public static string Of(string? text)
    {
        var plain = new string((text ?? string.Empty).Normalize(NormalizationForm.FormD).Where(character => char.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        return string.Join(' ', plain.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
