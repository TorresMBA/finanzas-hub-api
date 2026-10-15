namespace PersonalFinance.BuildingBlocks.Application.Abstractions;

/// <summary>
/// One sheet of a workbook. A cell is a <see cref="string"/>, a <see cref="decimal"/>, an <see cref="int"/>, a
/// <see cref="DateOnly"/> or null; the first row is taken as the header.
/// </summary>
public sealed record SpreadsheetSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>Writes and reads Excel workbooks (.xlsx), so the modules never depend on the library that does it.</summary>
public interface ISpreadsheets
{
    byte[] Write(IReadOnlyList<SpreadsheetSheet> sheets);

    /// <summary>
    /// The cells of the first sheet as text, row by row: dates as <c>2026-10-01</c> and numbers with a point for
    /// decimals. Null when the content is not a workbook.
    /// </summary>
    IReadOnlyList<IReadOnlyList<string>>? ReadFirstSheet(byte[] content);

    /// <summary>Whether the content looks like a workbook: an .xlsx file is a zip archive.</summary>
    static bool IsWorkbook(byte[] content) => content is [0x50, 0x4B, 0x03, 0x04, ..];
}
