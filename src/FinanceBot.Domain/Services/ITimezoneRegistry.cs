namespace FinanceBot.Domain.Services;

/// <summary>
/// Реестр таймзон. Обёртка над TimeZoneInfo.FindSystemTimeZoneById с дефолтом и валидацией.
/// </summary>
public interface ITimezoneRegistry
{
    /// <summary>Получить TimeZoneInfo по IANA-имени. Бросает, если не найдено.</summary>
    TimeZoneInfo Get(string ianaName);

    /// <summary>Try-вариант, без исключений.</summary>
    bool TryGet(string ianaName, out TimeZoneInfo? timezone);

    /// <summary>Дефолтная серверная таймзона.</summary>
    TimeZoneInfo Default { get; }

    /// <summary>Является ли строка валидным IANA-именем.</summary>
    bool IsValid(string ianaName);
}
