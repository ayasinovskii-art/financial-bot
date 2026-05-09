using System.Globalization;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>Stage 7: команда /settings (показать / изменить / сбросить).</summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage7()
    {
        Receive<SettingsReplyResult>(OnSettingsReplyResult);
        Receive<SettingsSnapshotResult>(OnSettingsSnapshotResult);
    }

    partial void HandleSettings(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            _log.Error("UserShardRegion not registered.");
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var trimmed = args.Trim();

        if (trimmed.Length == 0)
        {
            // Показать все настройки.
            var envelope = new ShardEnvelope(userId.ToString("N"), new GetUserSnapshot(userId));
            var self = Self;
            userShard.Ask<object>(envelope, AskTimeout)
                .ContinueWith(t => (SettingsSnapshotResult)new SettingsSnapshotResult(update,
                    t.IsCompletedSuccessfully ? t.Result as UserSnapshot : null,
                    t.IsFaulted ? t.Exception : null))
                .PipeTo(self);
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
                    Self.Tell(new OutgoingTelegramReply(update.ChatId,
                        TelegramReplies.SettingsUnknownKey(tokens[1])));
                    return;
                }
                maybeKey = k;
            }

            DispatchSettings(userShard, update, userId, new ResetSettings(userId, maybeKey));
            return;
        }

        // /settings <key> <value>
        var kv = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (kv.Length != 2)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.SettingsUsage()));
            return;
        }

        if (!SettingsKeyExtensions.TryFromWireName(kv[0], out var key))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.SettingsUnknownKey(kv[0])));
            return;
        }

        DispatchSettings(userShard, update, userId, new UpdateSettings(userId, key, kv[1]));
    }

    private void DispatchSettings(IActorRef userShard, IncomingTelegramUpdate update, Guid userId, object command)
    {
        var envelope = new ShardEnvelope(userId.ToString("N"), command);
        var self = Self;
        userShard.Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (SettingsReplyResult)new SettingsReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnSettingsReplyResult(SettingsReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Settings command failed for telegramId={TelegramId}.", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        var text = msg.Reply switch
        {
            SettingsUpdated u => TelegramReplies.SettingsUpdated(u.Key, u.NewValue),
            SettingsValidationFailed f => TelegramReplies.SettingsInvalid(f.Key, f.Reason),
            SettingsResetCompleted r => TelegramReplies.SettingsReset(r.Key),
            _ => "Не понял ответа от UserActor."
        };

        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, text));
    }

    private void OnSettingsSnapshotResult(SettingsSnapshotResult msg)
    {
        if (msg.Exception is not null || msg.Snapshot is null)
        {
            _log.Error(msg.Exception, "GetSnapshot failed for telegramId={TelegramId}.", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Текущие настройки:");
        foreach (var key in SettingsKeyExtensions.All)
        {
            var wire = key.ToWireName();
            var value = msg.Snapshot.Settings.GetValueOrDefault(wire);
            sb.Append("- ").Append(wire).Append(": ")
              .Append(value is null ? "(default)" : value.ToString(CultureInfo.InvariantCulture))
              .AppendLine();
        }
        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, sb.ToString().TrimEnd()));
    }

    private sealed record SettingsReplyResult(IncomingTelegramUpdate Update, object? Reply, AggregateException? Exception);
    private sealed record SettingsSnapshotResult(IncomingTelegramUpdate Update, UserSnapshot? Snapshot, AggregateException? Exception);
}
