namespace FinanceBot.Application.Csv;

public sealed record ParsedImportRow(
    decimal Amount,
    DateOnly Date,
    string Description);

public sealed record CsvParseResult(
    IReadOnlyList<ParsedImportRow> Rows,
    int SkippedCount);

public interface ICsvImportParser
{
    CsvParseResult Parse(string csvText);
}
