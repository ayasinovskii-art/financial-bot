using FinanceBot.Infrastructure.Csv;
using Xunit;

namespace FinanceBot.Application.Tests.Csv;

public sealed class TinkoffCsvParserTests
{
    private static readonly TinkoffCsvParser Parser = new();

    private const string ValidHeader =
        "Дата операции;Дата платежа;Номер карты;Статус;Сумма операции;Валюта операции;Сумма платежа;Валюта платежа;Кэшбэк;Категория;MCC;Описание;Бонусы (начислено);Бонусы (списано);Округление на инвесткопилку;Сумма операции с округлением";

    private static string MakeRow(string date = "01.01.2026 12:00:00", string status = "OK",
        string amount = "-500,00", string currency = "RUB", string description = "Продукты")
        => $"{date};01.01.2026;*1234;{status};-500,00;RUB;{amount};{currency};0;Еда;5411;{description};0;0;0;0";

    [Fact]
    public void Valid_csv_returns_parsed_rows()
    {
        var csv = $"{ValidHeader}\n{MakeRow()}";

        var result = Parser.Parse(csv);

        Assert.Single(result.Rows);
        Assert.Equal(500m, result.Rows[0].Amount);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Rows[0].Date);
        Assert.Equal("Продукты", result.Rows[0].Description);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void Header_only_returns_empty_result()
    {
        var result = Parser.Parse(ValidHeader);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void Non_rub_row_is_skipped()
    {
        var csv = $"{ValidHeader}\n{MakeRow(currency: "USD")}";

        var result = Parser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void Non_ok_status_row_is_skipped()
    {
        var csv = $"{ValidHeader}\n{MakeRow(status: "FAILED")}";

        var result = Parser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void Positive_amount_income_row_is_skipped()
    {
        var csv = $"{ValidHeader}\n{MakeRow(amount: "1000,00")}";

        var result = Parser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void Intra_batch_duplicates_are_deduplicated()
    {
        var row = MakeRow();
        var csv = $"{ValidHeader}\n{row}\n{row}";

        var result = Parser.Parse(csv);

        Assert.Single(result.Rows);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void Multiple_valid_rows_all_returned()
    {
        var row1 = MakeRow(description: "Кофе", amount: "-150,00");
        var row2 = MakeRow(date: "02.01.2026 10:00:00", description: "Такси", amount: "-400,00");
        var csv = $"{ValidHeader}\n{row1}\n{row2}";

        var result = Parser.Parse(csv);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void Empty_string_returns_empty_result()
    {
        var result = Parser.Parse(string.Empty);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void Missing_required_columns_returns_empty_result()
    {
        var csv = "Column1;Column2;Column3\nval1;val2;val3";

        var result = Parser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.SkippedCount);
    }
}
