using System.Globalization;
using System.Text;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class SettingsHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Settings;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var trimmed = ctx.ArgumentLine.Trim();

        if (trimmed.Length == 0)
        {
            // Показать текущие настройки.
            ctx.AskAndReplyText(shard, new ShardEnvelope(userId.ToString("N"), new GetUserSnapshot(userId)),
                reply => reply is UserSnapshot snap ? FormatSnapshot(snap) : "Внутренняя ошибка.",
                "GetSnapshot");
            return;
        }

        // /settings reset [<key>]
        var tokens = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            SettingsKey? maybeKey = null;
            if (tokens.Length == 2)
            {
                if (!SettingsKeyExtensions.TryFromWireName(tokens[1], out var k))
                {
                    ctx.Reply(TelegramReplies.SettingsUnknownKey(tokens[1]));
                    return;
                }
                maybeKey = k;
            }
            Dispatch(ctx, shard, userId, new ResetSettings(userId, maybeKey));
            return;
        }

        // /settings <key> <value>
        var kv = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (kv.Length != 2)
        {
            ctx.Reply(TelegramReplies.SettingsUsage());
            return;
        }

        if (!SettingsKeyExtensions.TryFromWireName(kv[0], out var key))
        {
            ctx.Reply(TelegramReplies.SettingsUnknownKey(kv[0]));
            return;
        }

        Dispatch(ctx, shard, userId, new UpdateSettings(userId, key, kv[1]));
    }

    private static void Dispatch(TelegramCommandContext ctx, Akka.Actor.IActorRef shard, Guid userId, object command)
        => ctx.AskShardAndReplyText(shard, userId, command, reply => reply switch
        {
            SettingsUpdated u => TelegramReplies.SettingsUpdated(u.Key, u.NewValue),
            SettingsValidationFailed f => TelegramReplies.SettingsInvalid(f.Key, f.Reason),
            SettingsResetCompleted r => TelegramReplies.SettingsReset(r.Key),
            _ => "Не понял ответа от UserActor."
        }, "Settings");

    private static string FormatSnapshot(UserSnapshot snap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Текущие настройки:");
        foreach (var key in SettingsKeyExtensions.All)
        {
            var wire = key.ToWireName();
            var value = snap.Settings.GetValueOrDefault(wire);
            sb.Append("- ").Append(wire).Append(": ")
              .Append(value is null ? "(default)" : value.ToString(CultureInfo.InvariantCulture))
              .AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
