using System.Globalization;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Commands.AccessControl;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

internal static class WhitelistShared
{
    public static bool RequireAdmin(TelegramCommandContext ctx)
    {
        if (ctx.Allowed.Role == AccessRole.Admin)
        {
            return true;
        }
        ctx.Reply(TelegramReplies.AdminOnly());
        return false;
    }

    public static bool TryParseTelegramId(string args, out long telegramId)
        => long.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out telegramId)
           && telegramId > 0;

    public static void Ask(TelegramCommandContext ctx, object command)
    {
        var ac = ctx.GetShard<AccessControlSingletonMarker>();
        if (ac is null)
        {
            return;
        }
        ctx.AskAndReplyText(ac, command, reply => reply switch
        {
            AccessControlReply.NotAdmin => TelegramReplies.AdminOnly(),
            AccessControlReply.Whitelisted w => TelegramReplies.UserAdded(w.TelegramId),
            AccessControlReply.AlreadyWhitelisted a => TelegramReplies.UserAlreadyAdded(a.TelegramId),
            AccessControlReply.Revoked r => TelegramReplies.UserRemoved(r.TelegramId),
            AccessControlReply.NotWhitelisted n => TelegramReplies.UserNotInWhitelist(n.TelegramId),
            AccessControlReply.WhitelistList list => TelegramReplies.WhitelistList(list.Entries, list.Admins),
            _ => "Не понял ответа AccessControl."
        }, "AccessControl");
    }
}

public sealed class AddUserHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.AddUser;
    public void Execute(TelegramCommandContext ctx)
    {
        if (!WhitelistShared.RequireAdmin(ctx)) return;
        if (!WhitelistShared.TryParseTelegramId(ctx.ArgumentLine, out var id))
        {
            ctx.Reply(TelegramReplies.AdminUsage("/adduser <telegram_id>"));
            return;
        }
        WhitelistShared.Ask(ctx, new WhitelistUser(ctx.Update.TelegramId, id));
    }
}

public sealed class RemoveUserHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.RemoveUser;
    public void Execute(TelegramCommandContext ctx)
    {
        if (!WhitelistShared.RequireAdmin(ctx)) return;
        if (!WhitelistShared.TryParseTelegramId(ctx.ArgumentLine, out var id))
        {
            ctx.Reply(TelegramReplies.AdminUsage("/removeuser <telegram_id>"));
            return;
        }
        WhitelistShared.Ask(ctx, new RevokeUser(ctx.Update.TelegramId, id));
    }
}

public sealed class ListUsersHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.ListUsers;
    public void Execute(TelegramCommandContext ctx)
    {
        if (!WhitelistShared.RequireAdmin(ctx)) return;
        WhitelistShared.Ask(ctx, new ListWhitelisted());
    }
}
