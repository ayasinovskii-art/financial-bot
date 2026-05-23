using Akka.Actor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Integration.Tests.Recovery;

/// <summary>
/// Защитный тест перед рефакторингом UserActor: проигрываем команды → стопаем актор →
/// поднимаем новый с тем же PersistenceId → проверяем что состояние восстановилось из журнала.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserActorRecoveryIntegrationTests(PostgresContainerFixture fixture)
    : PostgresIntegrationTestBase(fixture)
{
    private ActorSystem? _system;
    private Microsoft.Extensions.Hosting.IHost? _host;

    [SkippableFact]
    public async Task State_is_restored_from_journal_after_actor_restart()
    {
        (_host, _system) = await AkkaSystemBootstrap.CreateAsync("recovery-test", ConnectionString);

        var userId = Guid.NewGuid();
        const long telegramId = 555_777_999;

        // Раунд 1: регистрация + смена пары настроек.
        var actor1 = _system.ActorOf(UserActor.CreateProps(userId), $"user-{userId:N}-1");
        var ackReg = await actor1.Ask<object>(new RegisterUser(userId, telegramId, "Europe/Moscow"),
            TimeSpan.FromSeconds(15));
        ackReg.Should().BeOfType<UserRegistrationCompleted>();

        var ackS1 = await actor1.Ask<object>(new UpdateSettings(userId, SettingsKey.Allocation, "60/20/20"),
            TimeSpan.FromSeconds(15));
        ackS1.Should().BeOfType<SettingsUpdated>();

        var ackS2 = await actor1.Ask<object>(new UpdateSettings(userId, SettingsKey.Timezone, "Asia/Yerevan"),
            TimeSpan.FromSeconds(15));
        ackS2.Should().BeOfType<SettingsUpdated>();

        await actor1.GracefulStop(TimeSpan.FromSeconds(10));

        // Раунд 2: новый актор с тем же PersistenceId должен поднять состояние из journal.
        var actor2 = _system.ActorOf(UserActor.CreateProps(userId), $"user-{userId:N}-2");
        var snapshot = await actor2.Ask<UserSnapshot>(new GetUserSnapshot(userId), TimeSpan.FromSeconds(15));

        snapshot.IsRegistered.Should().BeTrue();
        snapshot.TelegramId.Should().Be(telegramId);
        snapshot.Settings.Should().ContainKey(SettingsKey.Allocation.ToWireName())
            .WhoseValue.Should().Be("60/20/20");
        snapshot.Settings.Should().ContainKey(SettingsKey.Timezone.ToWireName())
            .WhoseValue.Should().Be("Asia/Yerevan");
    }

    public override async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await base.DisposeAsync();
    }
}
