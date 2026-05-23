using Akka.Actor;
using FinanceBot.Application.Projections;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Infrastructure.Projections;
using FinanceBot.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Integration.Tests.Projections;

/// <summary>
/// A1-регрессия: UserRegistered, прошедший через реальный Postgres journal + event-tagger,
/// должен быть подобран <see cref="UsersListProjection"/> и попасть в <c>app.users</c>.
/// До фикса handshake в ProjectionBase этот тест был красным (Sink.ActorRefWithAck зависал на init).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UsersListProjectionIntegrationTests(PostgresContainerFixture fixture)
    : PostgresIntegrationTestBase(fixture)
{
    private ActorSystem? _system;
    private Microsoft.Extensions.Hosting.IHost? _host;

    [SkippableFact]
    public async Task UserRegistered_event_is_projected_into_app_users()
    {
        (_host, _system) = await AkkaSystemBootstrap.CreateAsync("projection-test", ConnectionString);

        var dbFactory = new TestDbContextFactory(ConnectionString);
        var offsetStore = new ProjectionOffsetStore(dbFactory);
        var writer = new UsersReadModelWriter(dbFactory);

        var projection = _system.ActorOf(
            Props.Create(() => new UsersListProjection(offsetStore, writer)),
            "users-list-projection");

        var userId = Guid.NewGuid();
        var user = _system.ActorOf(UserActor.CreateProps(userId), $"user-{userId:N}");

        const long telegramId = 123_456_789;
        var register = new RegisterUser(userId, telegramId, "Europe/Moscow");

        var ack = await user.Ask<object>(register, TimeSpan.FromSeconds(15));
        ack.Should().BeOfType<UserRegistrationCompleted>();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var db = CreateDbContext();
            var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Users, u => u.UserId == userId);
            if (row is not null)
            {
                row.TelegramId.Should().Be(telegramId);
                row.Timezone.Should().Be("Europe/Moscow");
                _ = projection;
                return;
            }
            await Task.Delay(250);
        }

        Assert.Fail("Проекция не записала строку в app.users за 15 секунд.");
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
