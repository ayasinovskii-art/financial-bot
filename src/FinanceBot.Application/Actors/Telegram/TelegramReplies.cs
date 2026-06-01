using System.Globalization;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Шаблоны текстовых ответов бота. Вынесено отдельно, чтобы было удобно ревьюить и переводить.
/// </summary>
public static class TelegramReplies
{
    public static string Welcome(string firstName)
        => $"Привет, {firstName}! 👋\n" +
           "Я — финансовый бот. Помогу вести бюджет 50/25/25, отслеживать траты и планировать накопления.\n\n" +
           "Команды:\n" +
           "/help — справка\n" +
           "/whoami — мой Telegram ID\n" +
           "/cancel — отменить текущий диалог";

    public static string AccessDenied(long telegramId)
        => $"Доступ ограничен. Попроси админа добавить твой ID: `{telegramId}`";

    public static string Help()
        => """
           Доступные команды:

           Основное:
           /start — регистрация / приветствие
           /help — эта справка
           /whoami — мой Telegram ID
           /cancel — отменить текущий диалог

           Доходы и расходы (будут доступны позже):
           /income [<date>] <amount> [<description>] — записать доход
           /expense [<date>] <amount> <description> — записать трату
           /expense_day [<date>] <amount> — итог дня
           /correct — исправить категорию

           Шаблоны и планы:
           /template add <name> <amount> <schedule> [<category>]
           /template list / /template remove <name>
           /plan add <amount> <date> <description>
           /plan list / /plan remove <id>

           Советы и графики:
           /advice [week|month]
           /chart <category|daily|buckets|savings>
           /report [period]
           /savings <amount>

           Настройки:
           /settings — показать
           /settings <key> <value> — изменить
           /settings reset [<key>]

           Админ:
           /adduser <telegram_id>
           /removeuser <telegram_id>
           /listusers
           """;

    public static string WhoAmI(long telegramId)
        => $"Твой Telegram ID: `{telegramId}`";

    public static string CancelAck() => "Отменено. Ничего не сохранил.";

    public static string UnknownCommand()
        => "Не понял команду. Посмотри /help для списка доступных команд.";

    public static string AlreadyRegistered()
        => "Ты уже зарегистрирован. Используй /help для списка команд.";

    public static string AdminOnly() => "Команда доступна только администраторам.";

    public static string AdminUsage(string usage)
        => $"Не разобрал аргументы. Формат: `{usage}`";

    public static string UserAdded(long telegramId) => $"Пользователь `{telegramId}` добавлен в whitelist.";

    public static string UserAlreadyAdded(long telegramId) => $"Пользователь `{telegramId}` уже в whitelist.";

    public static string UserRemoved(long telegramId) => $"Пользователь `{telegramId}` удалён из whitelist.";

    public static string UserNotInWhitelist(long telegramId) => $"Пользователя `{telegramId}` нет в whitelist.";

    public static string SettingsUsage()
        => "Формат: `/settings <key> <value>` или `/settings reset [<key>]`. См. /help.";

    public static string SettingsUnknownKey(string raw)
        => $"Неизвестный ключ настройки `{raw}`. Доступные ключи: timezone, evening_time, salary_days, shift_rule, silence_deadline_hours, auto_confirm_recurring, auto_confirm_on_silence, period_type, allocation, bucket_mapping, salary_amount.";

    public static string SettingsUpdated(FinanceBot.Domain.ValueObjects.SettingsKey key, string? newValue)
        => $"Настройка `{key.ToWireName()}` обновлена: `{newValue ?? "(default)"}`.";

    public static string SettingsInvalid(FinanceBot.Domain.ValueObjects.SettingsKey key, string reason)
        => $"Не удалось обновить `{key.ToWireName()}`: {reason}";

    public static string SettingsReset(FinanceBot.Domain.ValueObjects.SettingsKey? key)
        => key is null
            ? "Все настройки сброшены к значениям по умолчанию."
            : $"Настройка `{key.Value.ToWireName()}` сброшена к default.";

    public static string IncomeUsage()
        => "Формат: `/income [<YYYY-MM-DD>] <amount> [<description>]`. Например: `/income 50000 зп`.";

    public static string IncomeAccepted(IncomeAccepted a)
        => $"Доход принят. Период с {a.PeriodStartDate:yyyy-MM-dd}, итого: {Format(a.TotalIncome)} ₽.\n" +
           $"Аллокация — Essentials: {Format(a.AllocationEssentials)} ₽, " +
           $"Fun: {Format(a.AllocationFun)} ₽, Deposit: {Format(a.AllocationDeposit)} ₽.";

    public static string ExpenseUsage()
        => "Формат: `/expense [<YYYY-MM-DD>] <amount> <description>`. Например: `/expense 750 обед`.";

    public static string ExpenseAccepted(ExpenseAccepted a)
    {
        var bucketLine = a.Bucket switch
        {
            Bucket.Essentials =>
                $"Essentials: потрачено {Format(a.SpentEssentials)} / {Format(a.AllocationEssentials)} ₽ " +
                $"(осталось {Format(a.AllocationEssentials - a.SpentEssentials)} ₽).",
            Bucket.Fun =>
                $"Fun: потрачено {Format(a.SpentFun)} / {Format(a.AllocationFun)} ₽ " +
                $"(осталось {Format(a.AllocationFun - a.SpentFun)} ₽).",
            Bucket.Deposit =>
                $"Deposit: потрачено {Format(a.SpentDeposit)} / {Format(a.AllocationDeposit)} ₽.",
            _ => string.Empty
        };

        return $"Записал {Format(a.Amount)} ₽ → `{a.Category}` ({a.Bucket}).\n{bucketLine}";
    }

    public static string TemplateUsage()
        => "Команды: `/template add <name> <amount> <schedule> [<category>]`, `/template list`, `/template remove <name>`.\n" +
           "Schedule: `weekdays` / `daily` / `dow:1,3,5` / `dom:1,15`.";

    public static string PlanUsage()
        => "Команды: `/plan add <amount> <YYYY-MM-DD> <description>`, `/plan list`, `/plan remove <id>`.";

    public static string SavingsUsage()
        => "Формат: `/savings <amount>`. Подтверждает фактический перевод на накопления.";

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string WhitelistList(WhitelistEntry[] entries, long[] admins)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Whitelist:");
        if (entries.Length == 0)
        {
            sb.AppendLine("(пусто)");
        }
        else
        {
            foreach (var e in entries)
            {
                sb.Append("- ").Append(e.TelegramId).Append(" (added by ").Append(e.AddedBy)
                  .Append(" at ").Append(e.AddedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)).AppendLine(")");
            }
        }
        sb.AppendLine();
        sb.AppendLine("Admins (из конфига):");
        foreach (var a in admins)
        {
            sb.Append("- ").AppendLine(a.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString().TrimEnd();
    }
}
