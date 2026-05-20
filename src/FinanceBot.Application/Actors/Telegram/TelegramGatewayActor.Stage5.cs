using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Логика Stage 5+: парсинг Telegram-команд, проверка whitelist и роутинг в shard region User.
/// Stage'ы 6–10 расширяют диспетчер дополнительными case'ами и partial-handler-методами,
/// реализованными в файлах <c>TelegramGatewayActor.Stage{N}.cs</c>.
/// </summary>
public sealed partial class TelegramGatewayActor
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);

    partial void WireStage5()
    {
        Receive<IncomingTelegramUpdate>(HandleIncomingUpdate);
        Receive<IncomingCallbackQuery>(HandleIncomingCallback);
        Receive<OutgoingTelegramReply>(HandleOutgoingReply);
        Receive<OutgoingInlineKeyboard>(HandleOutgoingKeyboard);
        Receive<OutgoingCallbackAck>(HandleOutgoingCallbackAck);

        Receive<AccessCheckResult>(OnAccessCheckResult);
        Receive<RegisterReplyResult>(OnRegisterReplyResult);

        WireStage6();
        WireStage7();
        WireStage8();
        WireStage9();
        WireStage11();
        WireStage13();
        WireStage14();
        WireStage15();
        WireStage19();
        WireStage20();
        WireStage21();
    }

    partial void WireStage6();
    partial void WireStage7();
    partial void WireStage8();
    partial void WireStage9();
    partial void WireStage11();
    partial void WireStage13();
    partial void WireStage14();
    partial void WireStage15();
    partial void WireStage19();
    partial void WireStage20();
    partial void WireStage21();

    partial void HandleAddUser(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleRemoveUser(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleListUsers(IncomingTelegramUpdate update, AccessDecision.Allowed allowed);
    partial void HandleSettings(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleIncome(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleExpense(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleExpenseDay(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleCorrect(IncomingTelegramUpdate update, AccessDecision.Allowed allowed);
    partial void HandleFreeText(IncomingTelegramUpdate update, AccessDecision.Allowed allowed);
    partial void HandleCorrectionCallback(IncomingCallbackQuery callback);
    partial void HandleTemplate(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandlePlan(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleSavings(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleAdvice(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleChart(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);
    partial void HandleReport(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed);

    private void HandleIncomingUpdate(IncomingTelegramUpdate update)
    {
        var parsed = TelegramCommandParser.TryParse(update.Text);
        var registry = ActorRegistry.For(Context.System);

        if (!registry.TryGet<AccessControlSingletonMarker>(out var accessControl))
        {
            _log.Warning("AccessControlActor not available; dropping update {UpdateId}.", update.UpdateId);
            return;
        }

        var self = Self;
        accessControl
            .Ask<AccessDecision>(new Domain.Commands.AccessControl.IsAllowed(update.TelegramId), AskTimeout)
            .ContinueWith(t => (AccessCheckResult)new AccessCheckResult(update, parsed,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnAccessCheckResult(AccessCheckResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Access check failed for telegramId={TelegramId}", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        switch (msg.Decision)
        {
            case AccessDecision.Denied:
                Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, TelegramReplies.AccessDenied(msg.Update.TelegramId)));
                return;

            case AccessDecision.Allowed allowed:
                RouteAuthorized(msg.Update, msg.Parsed, allowed);
                return;
        }
    }

    private void RouteAuthorized(IncomingTelegramUpdate update, ParsedTelegramCommand? parsed, AccessDecision.Allowed allowed)
    {
        if (parsed is null)
        {
            HandleFreeText(update, allowed);
            return;
        }

        switch (parsed.Kind)
        {
            case TelegramCommandKind.Start:
                HandleStart(update);
                break;
            case TelegramCommandKind.Help:
                Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.Help()));
                break;
            case TelegramCommandKind.WhoAmI:
                Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.WhoAmI(update.TelegramId)));
                break;
            case TelegramCommandKind.Cancel:
                HandleCancel(update);
                break;

            case TelegramCommandKind.AddUser:
                HandleAddUser(update, parsed.ArgumentLine, allowed);
                break;
            case TelegramCommandKind.RemoveUser:
                HandleRemoveUser(update, parsed.ArgumentLine, allowed);
                break;
            case TelegramCommandKind.ListUsers:
                HandleListUsers(update, allowed);
                break;

            case TelegramCommandKind.Settings:
                HandleSettings(update, parsed.ArgumentLine, allowed);
                break;

            case TelegramCommandKind.Income:
                HandleIncome(update, parsed.ArgumentLine, allowed);
                break;
            case TelegramCommandKind.Expense:
                HandleExpense(update, parsed.ArgumentLine, allowed);
                break;
            case TelegramCommandKind.ExpenseDay:
                HandleExpenseDay(update, parsed.ArgumentLine, allowed);
                break;
            case TelegramCommandKind.Correct:
                HandleCorrect(update, allowed);
                break;

            case TelegramCommandKind.Template:
                HandleTemplate(update, parsed.ArgumentLine, allowed);
                break;
            case TelegramCommandKind.Plan:
                HandlePlan(update, parsed.ArgumentLine, allowed);
                break;
            case TelegramCommandKind.Savings:
                HandleSavings(update, parsed.ArgumentLine, allowed);
                break;

            case TelegramCommandKind.Advice:
                HandleAdvice(update, parsed.ArgumentLine, allowed);
                break;

            case TelegramCommandKind.Chart:
                HandleChart(update, parsed.ArgumentLine, allowed);
                break;

            case TelegramCommandKind.Report:
                HandleReport(update, parsed.ArgumentLine, allowed);
                break;

            default:
                Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.UnknownCommand()));
                break;
        }
    }

    private void HandleStart(IncomingTelegramUpdate update)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            _log.Error("UserShardRegion not registered.");
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var register = new RegisterUser(userId, update.TelegramId, TimeZoneInfo.Local.Id);
        var envelope = new ShardEnvelope(userId.ToString("N"), register);

        var self = Self;
        userShard
            .Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (RegisterReplyResult)new RegisterReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnRegisterReplyResult(RegisterReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Register failed for telegramId={TelegramId}", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Не удалось зарегистрировать. Попробуй позже."));
            return;
        }

        switch (msg.Reply)
        {
            case UserRegistrationCompleted:
                {
                    var firstName = msg.Update.FirstName ?? "друг";
                    Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, TelegramReplies.Welcome(firstName)));
                    break;
                }
            case UserAlreadyRegistered:
                {
                    Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, TelegramReplies.AlreadyRegistered()));
                    break;
                }
            default:
                _log.Warning("Unexpected reply from UserActor: {ReplyType}", msg.Reply?.GetType().Name);
                break;
        }
    }

    private void HandleCancel(IncomingTelegramUpdate update)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var envelope = new ShardEnvelope(userId.ToString("N"), new Cancel(userId));
        userShard.Tell(envelope);

        Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.CancelAck()));
    }

    private void HandleOutgoingReply(OutgoingTelegramReply reply)
    {
        Context.System.EventStream.Publish(reply);
    }

    private void HandleOutgoingKeyboard(OutgoingInlineKeyboard kb)
    {
        Context.System.EventStream.Publish(kb);
    }

    private void HandleOutgoingCallbackAck(OutgoingCallbackAck ack)
    {
        Context.System.EventStream.Publish(ack);
    }

    private void HandleIncomingCallback(IncomingCallbackQuery cb)
    {
        // Stage 11 routing — пока единственный prefix "correct:".
        HandleCorrectionCallback(cb);
    }

    private sealed record AccessCheckResult(
        IncomingTelegramUpdate Update,
        ParsedTelegramCommand? Parsed,
        AccessDecision? Decision,
        AggregateException? Exception);

    private sealed record RegisterReplyResult(
        IncomingTelegramUpdate Update,
        object? Reply,
        AggregateException? Exception);
}
