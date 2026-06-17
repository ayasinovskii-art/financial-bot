using FinanceBot.Domain.ValueObjects;
using Xunit;

namespace FinanceBot.Domain.Tests.ValueObjects;

public sealed class ExpenseSourceExtensionsTests
{
    [Theory]
    [InlineData(ExpenseSource.Manual, "manual")]
    [InlineData(ExpenseSource.Memory, "memory")]
    [InlineData(ExpenseSource.Rules, "rules")]
    [InlineData(ExpenseSource.Claude, "claude")]
    [InlineData(ExpenseSource.Fallback, "fallback")]
    [InlineData(ExpenseSource.RecurringAuto, "recurring-auto")]
    [InlineData(ExpenseSource.PlannedConfirmed, "planned-confirmed")]
    [InlineData(ExpenseSource.CsvImport, "csv-import")]
    public void ToWireName_returns_expected_wire_name(ExpenseSource source, string expected)
    {
        Assert.Equal(expected, source.ToWireName());
    }
}
