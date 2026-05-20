using Akka.Persistence.Journal;
using FinanceBot.Domain.Events;
using FinanceBot.Domain.Events.Advisor;
using FinanceBot.Domain.Events.Budget;
using FinanceBot.Domain.Events.Categorization;
using FinanceBot.Domain.Events.Expense;
using FinanceBot.Domain.Events.Income;
using FinanceBot.Domain.Events.Reports;
using FinanceBot.Domain.Events.User;
using FinanceBot.Domain.Events.Whitelist;

namespace FinanceBot.Application.Configuration;

/// <summary>
/// IEventAdapter, навешивающий теги на доменные события согласно правилам из ТЗ §5.6.
/// Подключается в HOCON через <c>akka.persistence.journal.postgresql.event-adapters</c>.
/// </summary>
public sealed class EventTagger : IWriteEventAdapter
{
    public string Manifest(object evt) => string.Empty;

    public object ToJournal(object evt)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);

        if (evt is IUserScopedEvent userScoped)
        {
            tags.Add(PersistenceTags.ForUser(userScoped.UserId));
        }

        switch (evt)
        {
            case ExpenseReported:
            case ExpenseCategorizedAutomatically:
            case ExpenseCategoryConfirmed:
            case ExpenseCategoryCorrected:
            case ExpenseDeleted:
                tags.Add(PersistenceTags.Expense);
                tags.Add(PersistenceTags.Category);
                break;
            case IncomeReported:
            case IncomeReportRequested:
                tags.Add(PersistenceTags.Income);
                break;
            case BudgetPeriodStarted:
            case BudgetAllocated:
            case BudgetPeriodClosed:
            case BucketThresholdCrossed:
            case SavingsReported:
                tags.Add(PersistenceTags.Period);
                break;
            case CategorizationRequested:
            case CategorizationCompleted:
            case CategorizationFailed:
                tags.Add(PersistenceTags.Category);
                break;
            case UserWhitelisted:
            case UserRevoked:
                tags.Add(PersistenceTags.Whitelist);
                break;
            case UserSettingsUpdated:
                tags.Add(PersistenceTags.Settings);
                tags.Add(PersistenceTags.UserLifecycle);
                break;
            case UserRegistered:
                tags.Add(PersistenceTags.UserLifecycle);
                break;
            case ConsultationRequested:
            case ConsultationAnswered:
            case AdviceParked:
            case AdviceResumedWithFreshContext:
                tags.Add(PersistenceTags.Advisor);
                break;
            case ChartRequested:
            case ChartGenerated:
                tags.Add(PersistenceTags.Report);
                break;
        }

        return tags.Count == 0 ? evt : new Tagged(evt, tags);
    }
}
