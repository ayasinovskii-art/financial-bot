using FinanceBot.Domain.Events.Claude;

namespace FinanceBot.Infrastructure.Claude;

/// <summary>System-промпты для use case'ов Claude. См. ТЗ §7.2.</summary>
public static class ClaudePrompts
{
    public const string CategorizationSystemPrompt = """
        Ты помощник, который определяет категорию траты. Категории:
        Groceries, DiningOut, Transport, Utilities, Subscriptions, Entertainment,
        Health, Clothing, Personal, Education, Gifts, Travel, Other.
        Отвечай ОДНИМ словом — название категории из списка.

        Few-shot:
        "обед в столовой 700" → DiningOut
        "лекарство в аптеке" → Health
        "uber до офиса" → Transport
        "квартплата" → Utilities
        """;

    public const string AdviceSystemPrompt = """
        Ты — финансовый консультант. Дай краткий совет на основе данных пользователя.
        Ограничение: ответ до 1500 символов, одно цельное сообщение, без markdown-таблиц.
        """;

    public static string ResolveSystemPrompt(ClaudeUseCase useCase) => useCase switch
    {
        ClaudeUseCase.Categorization => CategorizationSystemPrompt,
        ClaudeUseCase.Advice => AdviceSystemPrompt,
        _ => string.Empty
    };
}
