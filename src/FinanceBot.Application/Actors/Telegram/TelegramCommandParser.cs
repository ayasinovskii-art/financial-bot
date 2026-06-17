namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Распознанный verb команды. Расширяется по мере добавления stage'ей.
/// </summary>
public enum TelegramCommandKind
{
    None = 0,
    Start = 1,
    Help = 2,
    WhoAmI = 3,
    Cancel = 4,

    // admin
    AddUser = 10,
    RemoveUser = 11,
    ListUsers = 12,

    // settings
    Settings = 20,

    // income
    Income = 30,

    // expenses
    Expense = 40,
    ExpenseDay = 41,
    Correct = 42,

    // recurring templates
    Template = 50,

    // planned expenses
    Plan = 60,

    // savings
    Savings = 70,

    // goals
    Goal = 75,

    // advisor
    Advice = 80,

    // charts
    Chart = 90,

    // report
    Report = 95,

    // admin: Claude API rate-limit info
    Tokens = 96,

    // stats: топ категорий за период
    Stats = 97,

    // export: CSV-выгрузка трат периода
    Export = 98,

    // import: инструкция по отправке CSV-файла
    Import = 100,

    // delete: удаление трат/доходов/целей
    Delete = 101,

    Unknown = 99
}

/// <summary>Распарсенная команда из текста сообщения Telegram.</summary>
public sealed record ParsedTelegramCommand(TelegramCommandKind Kind, string OriginalText, string ArgumentLine);

/// <summary>
/// Парсер telegram-команд. Полностью stateless.
/// </summary>
public static class TelegramCommandParser
{
    public static ParsedTelegramCommand? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return null;
        }

        var spaceIdx = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var commandPart = spaceIdx < 0 ? trimmed : trimmed[..spaceIdx];
        var argsPart = spaceIdx < 0 ? string.Empty : trimmed[(spaceIdx + 1)..].Trim();

        var atIdx = commandPart.IndexOf('@', StringComparison.Ordinal);
        if (atIdx >= 0)
        {
            commandPart = commandPart[..atIdx];
        }

        var verb = commandPart.AsSpan(1).ToString().ToLowerInvariant();
        var kind = verb switch
        {
            "start" => TelegramCommandKind.Start,
            "help" => TelegramCommandKind.Help,
            "whoami" => TelegramCommandKind.WhoAmI,
            "cancel" => TelegramCommandKind.Cancel,
            "adduser" => TelegramCommandKind.AddUser,
            "removeuser" => TelegramCommandKind.RemoveUser,
            "listusers" => TelegramCommandKind.ListUsers,
            "settings" => TelegramCommandKind.Settings,
            "income" => TelegramCommandKind.Income,
            "expense" => TelegramCommandKind.Expense,
            "expense_day" => TelegramCommandKind.ExpenseDay,
            "correct" => TelegramCommandKind.Correct,
            "template" => TelegramCommandKind.Template,
            "plan" => TelegramCommandKind.Plan,
            "savings" => TelegramCommandKind.Savings,
            "goal" => TelegramCommandKind.Goal,
            "advice" => TelegramCommandKind.Advice,
            "chart" => TelegramCommandKind.Chart,
            "report" => TelegramCommandKind.Report,
            "tokens" => TelegramCommandKind.Tokens,
            "stats" => TelegramCommandKind.Stats,
            "export" => TelegramCommandKind.Export,
            "import" => TelegramCommandKind.Import,
            "delete" => TelegramCommandKind.Delete,
            _ => TelegramCommandKind.Unknown
        };

        return new ParsedTelegramCommand(kind, text, argsPart);
    }
}
