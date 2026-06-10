using FinanceBot.Application.Actors.Reports;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Reports;

public sealed class ExpensesCsvBuilderTests
{
    private static ExpenseCsvRow Row(
        string date = "2026-06-05",
        decimal amount = 750m,
        string category = "Groceries",
        string? description = "обед")
        => new(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), amount, category, description);

    private static string[] Lines(string csv) => csv.Split("\r\n", StringSplitOptions.None);

    [Fact]
    public void Header_is_first_line()
    {
        var csv = ExpensesCsvBuilder.Build([Row()]);

        Lines(csv)[0].Should().Be("date,amount,category,description");
    }

    [Fact]
    public void Row_uses_iso_date_and_invariant_amount()
    {
        var csv = ExpensesCsvBuilder.Build([Row(date: "2026-06-05", amount: 1234.50m)]);

        Lines(csv)[1].Should().Be("2026-06-05,1234.50,Groceries,обед");
    }

    [Fact]
    public void Amount_has_no_thousands_separator()
    {
        var csv = ExpensesCsvBuilder.Build([Row(amount: 1234567.89m)]);

        csv.Should().Contain("1234567.89");
    }

    [Fact]
    public void Description_with_comma_is_quoted()
    {
        var csv = ExpensesCsvBuilder.Build([Row(description: "обед, кофе")]);

        Lines(csv)[1].Should().Be("2026-06-05,750.00,Groceries,\"обед, кофе\"");
    }

    [Fact]
    public void Description_with_quote_is_quoted_and_doubled()
    {
        var csv = ExpensesCsvBuilder.Build([Row(description: "кафе \"Ромашка\"")]);

        Lines(csv)[1].Should().Be("2026-06-05,750.00,Groceries,\"кафе \"\"Ромашка\"\"\"");
    }

    [Fact]
    public void Description_with_newline_is_quoted()
    {
        var csv = ExpensesCsvBuilder.Build([Row(description: "строка1\nстрока2")]);

        csv.Should().Contain("\"строка1\nстрока2\"");
    }

    [Fact]
    public void Null_description_becomes_empty_field()
    {
        var csv = ExpensesCsvBuilder.Build([Row(description: null)]);

        Lines(csv)[1].Should().Be("2026-06-05,750.00,Groceries,");
    }

    [Fact]
    public void Rows_keep_input_order()
    {
        var csv = ExpensesCsvBuilder.Build(
        [
            Row(date: "2026-06-01", description: "первая"),
            Row(date: "2026-06-02", description: "вторая"),
        ]);

        var lines = Lines(csv);
        lines[1].Should().Contain("первая");
        lines[2].Should().Contain("вторая");
    }

    [Fact]
    public void Empty_rows_produce_header_only()
    {
        var csv = ExpensesCsvBuilder.Build([]);

        csv.Should().Be("date,amount,category,description");
    }
}
