using System.Globalization;
using ClosedXML.Excel;
using PersonalFinance.BuildingBlocks.Application.Abstractions;

namespace PersonalFinance.Infrastructure.Spreadsheets;

/// <summary>Excel workbooks with ClosedXML.</summary>
internal sealed class ClosedXmlSpreadsheets : ISpreadsheets
{
    private const int MaxSheetNameLength = 31;

    public byte[] Write(IReadOnlyList<SpreadsheetSheet> sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var sheet in sheets)
        {
            var worksheet = workbook.Worksheets.Add(sheet.Name.Length > MaxSheetNameLength ? sheet.Name[..MaxSheetNameLength] : sheet.Name);
            for (var row = 0; row < sheet.Rows.Count; row++)
            {
                for (var column = 0; column < sheet.Rows[row].Count; column++)
                {
                    var cell = worksheet.Cell(row + 1, column + 1);
                    switch (sheet.Rows[row][column])
                    {
                        case decimal amount:
                            cell.Value = amount;
                            cell.Style.NumberFormat.Format = "#,##0.00";
                            break;
                        case int number:
                            cell.Value = number;
                            break;
                        case DateOnly date:
                            cell.Value = date.ToDateTime(TimeOnly.MinValue);
                            cell.Style.DateFormat.Format = "yyyy-mm-dd";
                            break;
                        case string text:
                            cell.Value = text;
                            break;
                    }
                }
            }

            if (sheet.Rows.Count > 0)
            {
                worksheet.Row(1).Style.Font.Bold = true;
                worksheet.SheetView.FreezeRows(1);
                worksheet.Columns().AdjustToContents(1, Math.Min(sheet.Rows.Count, 200));
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<IReadOnlyList<string>>? ReadFirstSheet(byte[] content)
    {
        if (!ISpreadsheets.IsWorkbook(content))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(content);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet?.RangeUsed() is not { } used)
            {
                return [];
            }

            var lastColumn = used.LastColumn().ColumnNumber();
            return used.Rows()
                .Select(row => (IReadOnlyList<string>)Enumerable.Range(1, lastColumn).Select(column => Text(row.WorksheetRow().Cell(column))).ToList())
                .ToList();
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException or InvalidOperationException
                                              or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
        {
            return null;
        }
    }

    /// <summary>A cell as the bank table reader expects it: ISO dates and invariant numbers.</summary>
    private static string Text(IXLCell cell)
    {
        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.DateTime => value.GetDateTime().ToString(value.GetDateTime().TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.Text => value.GetText().Trim(),
            _ => string.Empty
        };
    }
}
