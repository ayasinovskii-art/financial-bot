using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.UserTemplates.Messages;

/// <summary>Шаблон регулярной траты в read-форме (для list/queries).</summary>
public sealed record RecurringTemplateView(
    Guid TemplateId,
    string Name,
    decimal Amount,
    ScheduleSpec Schedule,
    Category? Category);

/// <summary>Reply: шаблон добавлен.</summary>
public sealed record TemplateAdded(Guid UserId, RecurringTemplateView Template);

/// <summary>Reply: шаблон удалён.</summary>
public sealed record TemplateRemoved(Guid UserId, string Name);

/// <summary>Reply: команда отклонена.</summary>
public sealed record TemplateRejected(Guid UserId, string Reason);

/// <summary>Reply: список шаблонов.</summary>
public sealed record TemplateList(Guid UserId, IReadOnlyList<RecurringTemplateView> Templates);

/// <summary>Запрос шаблонов, активных в указанную дату (для evening tick).</summary>
public sealed record GetRelevantTemplates(Guid UserId, DateOnly Date) : User.Messages.IUserShardMessage;

/// <summary>Список шаблонов, активных в дату.</summary>
public sealed record RelevantTemplatesList(Guid UserId, DateOnly Date, IReadOnlyList<RecurringTemplateView> Templates);
