using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Domain.Commands.AccessControl;
using FinanceBot.Domain.Events.Whitelist;
using Microsoft.Extensions.Options;

namespace FinanceBot.Application.Actors.AccessControl;

/// <summary>
/// Глобальный cluster singleton, отвечающий за whitelist-проверку и операции admin'а.
/// PersistenceId = "access-control".
/// AdminUserIds (из конфига) всегда allowed; остальные — только если есть активная запись.
/// </summary>
public sealed class AccessControlActor : ReceivePersistentActor
{
    private const int SnapshotEvery = 100;

    private readonly ILoggingAdapter _log;
    private readonly HashSet<long> _admins;
    private readonly HashSet<long> _whitelist = [];
    private readonly Dictionary<long, WhitelistEntry> _whitelistMeta = [];
    private long _eventsSinceSnapshot;

    public override string PersistenceId => "access-control";

    public AccessControlActor(IOptions<AuthOptions> options)
    {
        _log = Context.GetLogger();
        _admins = [.. options.Value.AdminUserIds];

        Recover<UserWhitelisted>(ApplyEvent);
        Recover<UserRevoked>(ApplyEvent);
        Recover<SnapshotOffer>(offer =>
        {
            if (offer.Snapshot is AccessControlSnapshot snap)
            {
                _whitelist.Clear();
                _whitelist.UnionWith(snap.WhitelistedTelegramIds);
                _whitelistMeta.Clear();
                foreach (var entry in snap.Entries)
                {
                    _whitelistMeta[entry.TelegramId] = entry;
                }
            }
        });

        Command<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));

        Command<IsAllowed>(cmd =>
        {
            var isAdmin = _admins.Contains(cmd.TelegramId);
            var allowed = isAdmin || _whitelist.Contains(cmd.TelegramId);
            var role = isAdmin ? AccessRole.Admin : AccessRole.User;
            Sender.Tell(allowed
                ? new AccessDecision.Allowed(cmd.TelegramId, role)
                : (object)new AccessDecision.Denied(cmd.TelegramId, "Not whitelisted."));
        });

        Command<WhitelistUser>(HandleWhitelistUser);
        Command<RevokeUser>(HandleRevokeUser);
        Command<ListWhitelisted>(_ => Sender.Tell(BuildListReply()));

        Command<SaveSnapshotSuccess>(_ => { });
        Command<SaveSnapshotFailure>(failure => _log.Error(failure.Cause, "AccessControl snapshot save failed."));

        CommandAny(msg => _log.Debug("AccessControlActor received unhandled {MessageType}", msg.GetType().Name));
    }

    private void HandleWhitelistUser(WhitelistUser cmd)
    {
        if (!_admins.Contains(cmd.AdminId))
        {
            Sender.Tell(new AccessControlReply.NotAdmin(cmd.AdminId));
            return;
        }

        if (_whitelist.Contains(cmd.TelegramId))
        {
            Sender.Tell(new AccessControlReply.AlreadyWhitelisted(cmd.TelegramId));
            return;
        }

        var evt = new UserWhitelisted(cmd.AdminId, cmd.TelegramId, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            _log.Info("User {TelegramId} whitelisted by admin {AdminId}.", persisted.TelegramId, persisted.AdminId);
            sender.Tell(new AccessControlReply.Whitelisted(persisted.TelegramId, persisted.AdminId));
        });
    }

    private void HandleRevokeUser(RevokeUser cmd)
    {
        if (!_admins.Contains(cmd.AdminId))
        {
            Sender.Tell(new AccessControlReply.NotAdmin(cmd.AdminId));
            return;
        }

        if (!_whitelist.Contains(cmd.TelegramId))
        {
            Sender.Tell(new AccessControlReply.NotWhitelisted(cmd.TelegramId));
            return;
        }

        var evt = new UserRevoked(cmd.AdminId, cmd.TelegramId, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            _log.Info("User {TelegramId} revoked by admin {AdminId}.", persisted.TelegramId, persisted.AdminId);
            sender.Tell(new AccessControlReply.Revoked(persisted.TelegramId, persisted.AdminId));
        });
    }

    private AccessControlReply.WhitelistList BuildListReply()
    {
        var entries = _whitelistMeta.Values
            .Where(e => e.RevokedAt is null)
            .Select(e => new WhitelistEntry(e.TelegramId, e.AddedBy, e.AddedAt, null))
            .ToArray();
        return new AccessControlReply.WhitelistList(entries, _admins.ToArray());
    }

    private void ApplyEvent(UserWhitelisted evt)
    {
        _whitelist.Add(evt.TelegramId);
        _whitelistMeta[evt.TelegramId] = new WhitelistEntry(evt.TelegramId, evt.AdminId, evt.OccurredAt, null);
    }

    private void ApplyEvent(UserRevoked evt)
    {
        _whitelist.Remove(evt.TelegramId);
        if (_whitelistMeta.TryGetValue(evt.TelegramId, out var existing))
        {
            _whitelistMeta[evt.TelegramId] = existing with { RevokedAt = evt.OccurredAt };
        }
    }

    private void MaybeSnapshot()
    {
        _eventsSinceSnapshot++;
        if (_eventsSinceSnapshot < SnapshotEvery)
        {
            return;
        }
        _eventsSinceSnapshot = 0;
        SaveSnapshot(new AccessControlSnapshot(
            _whitelist.ToArray(),
            _whitelistMeta.Values.ToArray()));
    }

    public static Props CreateProps(IOptions<AuthOptions> options)
        => Props.Create(() => new AccessControlActor(options));
}

/// <summary>Опции аутентификации (Auth-секция appsettings).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Список Telegram-id админов. Не может быть пустым по ТЗ §3.1.</summary>
    public long[] AdminUserIds { get; init; } = [];
}

/// <summary>Роль пользователя в системе доступа.</summary>
public enum AccessRole
{
    User = 0,
    Admin = 1
}

/// <summary>Результат проверки доступа.</summary>
public abstract record AccessDecision
{
    public sealed record Allowed(long TelegramId, AccessRole Role) : AccessDecision;
    public sealed record Denied(long TelegramId, string Reason) : AccessDecision;
}

/// <summary>Запись о whitelisted пользователе с метаданными.</summary>
public sealed record WhitelistEntry(long TelegramId, long AddedBy, DateTimeOffset AddedAt, DateTimeOffset? RevokedAt);

/// <summary>Снапшот состояния AccessControlActor.</summary>
public sealed record AccessControlSnapshot(long[] WhitelistedTelegramIds, WhitelistEntry[] Entries);

/// <summary>Ответы AccessControlActor на admin-команды.</summary>
public abstract record AccessControlReply
{
    public sealed record NotAdmin(long AdminId) : AccessControlReply;
    public sealed record Whitelisted(long TelegramId, long AdminId) : AccessControlReply;
    public sealed record AlreadyWhitelisted(long TelegramId) : AccessControlReply;
    public sealed record Revoked(long TelegramId, long AdminId) : AccessControlReply;
    public sealed record NotWhitelisted(long TelegramId) : AccessControlReply;
    public sealed record WhitelistList(WhitelistEntry[] Entries, long[] Admins) : AccessControlReply;
}
