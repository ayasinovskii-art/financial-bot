namespace FinanceBot.Domain.Commands;

/// <summary>Маркер команды (input в агрегат). Не содержит side-effects, только намерение.</summary>
public interface ICommand
{
}

/// <summary>Команда, направленная на конкретного пользователя.</summary>
public interface IUserScopedCommand : ICommand
{
    Guid UserId { get; }
}
