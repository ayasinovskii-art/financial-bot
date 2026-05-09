using System.Globalization;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Commands.AccessControl;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Stage 6: админ-команды /adduser, /removeuser, /listusers.
/// </summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage6()
    {
        Receive<AdminReplyResult>(OnAdminReplyResult);
    }

    partial void HandleAddUser(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        if (allowed.Role != AccessRole.Admin)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.AdminOnly()));
            return;
        }

        if (!TryParseTelegramId(args, out var targetId))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.AdminUsage("/adduser <telegram_id>")));
            return;
        }

        AskAccessControl(update, new WhitelistUser(update.TelegramId, targetId));
    }

    partial void HandleRemoveUser(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        if (allowed.Role != AccessRole.Admin)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.AdminOnly()));
            return;
        }

        if (!TryParseTelegramId(args, out var targetId))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.AdminUsage("/removeuser <telegram_id>")));
            return;
        }

        AskAccessControl(update, new RevokeUser(update.TelegramId, targetId));
    }

    partial void HandleListUsers(IncomingTelegramUpdate update, AccessDecision.Allowed allowed)
    {
        if (allowed.Role != AccessRole.Admin)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.AdminOnly()));
            return;
        }

        AskAccessControl(update, new ListWhitelisted());
    }

    private void AskAccessControl(IncomingTelegramUpdate update, object command)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<AccessControlSingletonMarker>(out var accessControl))
        {
            _log.Error("AccessControlActor not available.");
            return;
        }

        var self = Self;
        accessControl
            .Ask<AccessControlReply>(command, AskTimeout)
            .ContinueWith(t => (AdminReplyResult)new AdminReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnAdminReplyResult(AdminReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Admin command failed for telegramId={TelegramId}.", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        var text = msg.Reply switch
        {
            AccessControlReply.NotAdmin => TelegramReplies.AdminOnly(),
            AccessControlReply.Whitelisted w => TelegramReplies.UserAdded(w.TelegramId),
            AccessControlReply.AlreadyWhitelisted a => TelegramReplies.UserAlreadyAdded(a.TelegramId),
            AccessControlReply.Revoked r => TelegramReplies.UserRemoved(r.TelegramId),
            AccessControlReply.NotWhitelisted n => TelegramReplies.UserNotInWhitelist(n.TelegramId),
            AccessControlReply.WhitelistList list => TelegramReplies.WhitelistList(list.Entries, list.Admins),
            _ => "Не понял ответа AccessControl."
        };

        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, text));
    }

    private static bool TryParseTelegramId(string args, out long telegramId)
        => long.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out telegramId)
           && telegramId > 0;

    private sealed record AdminReplyResult(
        IncomingTelegramUpdate Update,
        AccessControlReply? Reply,
        AggregateException? Exception);
}
