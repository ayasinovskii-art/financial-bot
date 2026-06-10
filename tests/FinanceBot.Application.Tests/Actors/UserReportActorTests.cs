using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.Reports;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Domain.Commands.User;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>
/// Тесты UserReportActor: маршрутизация /report и /export через стаб IReportBuilder,
/// зарегистрированный в DI актор-системы (ServiceProviderHost).
/// </summary>
public sealed class UserReportActorTests : TestKit
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const long ChatId = 123456789L;

    private readonly StubReportBuilder _builder;

    public UserReportActorTests() : this(new StubReportBuilder())
    {
    }

    private UserReportActorTests(StubReportBuilder builder) : base(BuildSetup(builder))
    {
        _builder = builder;
    }

    private static ActorSystemSetup BuildSetup(StubReportBuilder builder)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReportBuilder>(builder);
        var provider = services.BuildServiceProvider();
        var bootstrap = BootstrapSetup.Create()
            .WithConfig(ConfigurationFactory.ParseString("akka.loglevel = WARNING"));
        return ActorSystemSetup.Create(bootstrap, DependencyResolverSetup.Create(provider));
    }

    private IActorRef CreateActorWithProbe(out Akka.TestKit.TestProbe probe)
    {
        probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(OutgoingTelegramReply));
        Sys.EventStream.Subscribe(probe, typeof(OutgoingTelegramDocument));
        return Sys.ActorOf(UserReportActor.CreateProps(UserId));
    }

    [Fact]
    public void Report_request_publishes_text_reply()
    {
        _builder.Report = new ReportResult(HasData: true, Text: "отчёт готов");
        var actor = CreateActorWithProbe(out var probe);

        actor.Tell(new EnrichedReportRequest(new RequestReport(UserId, Period: null), ChatId));

        var reply = probe.ExpectMsg<OutgoingTelegramReply>();
        reply.ChatId.Should().Be(ChatId);
        reply.Text.Should().Be("отчёт готов");
        _builder.LastPeriodsAgo.Should().Be(0);
    }

    [Fact]
    public void Report_failure_publishes_error_reply()
    {
        _builder.Throw = new InvalidOperationException("db down");
        var actor = CreateActorWithProbe(out var probe);

        actor.Tell(new EnrichedReportRequest(new RequestReport(UserId, Period: null), ChatId));

        var reply = probe.ExpectMsg<OutgoingTelegramReply>();
        reply.Text.Should().StartWith("Не удалось собрать отчёт");
    }

    [Fact]
    public void Export_request_publishes_document()
    {
        var content = new byte[] { 0xEF, 0xBB, 0xBF, 0x61 };
        _builder.Export = new ExportResult(HasData: true, FileName: "expenses-2026-06-01.csv", Content: content);
        var actor = CreateActorWithProbe(out var probe);

        actor.Tell(new EnrichedExportRequest(new RequestExport(UserId, Period: null), ChatId));

        var doc = probe.ExpectMsg<OutgoingTelegramDocument>();
        doc.ChatId.Should().Be(ChatId);
        doc.FileName.Should().Be("expenses-2026-06-01.csv");
        doc.Document.Should().Equal(content);
    }

    [Fact]
    public void Export_without_data_publishes_text_reply()
    {
        _builder.Export = new ExportResult(HasData: false, FileName: string.Empty, Content: []);
        var actor = CreateActorWithProbe(out var probe);

        actor.Tell(new EnrichedExportRequest(new RequestExport(UserId, Period: null), ChatId));

        var reply = probe.ExpectMsg<OutgoingTelegramReply>();
        reply.Text.Should().Contain("Нечего выгружать");
    }

    [Fact]
    public void Export_failure_publishes_error_reply()
    {
        _builder.Throw = new InvalidOperationException("db down");
        var actor = CreateActorWithProbe(out var probe);

        actor.Tell(new EnrichedExportRequest(new RequestExport(UserId, Period: null), ChatId));

        var reply = probe.ExpectMsg<OutgoingTelegramReply>();
        reply.Text.Should().StartWith("Не удалось собрать выгрузку");
    }

    [Theory]
    [InlineData("previous", 1)]
    [InlineData("prev", 1)]
    [InlineData("current", 0)]
    [InlineData("3", 3)]
    [InlineData("garbage", 0)]
    [InlineData("-2", 0)]
    public void Export_period_argument_is_parsed(string period, int expectedPeriodsAgo)
    {
        _builder.Export = new ExportResult(HasData: false, FileName: string.Empty, Content: []);
        var actor = CreateActorWithProbe(out var probe);

        actor.Tell(new EnrichedExportRequest(new RequestExport(UserId, period), ChatId));

        probe.ExpectMsg<OutgoingTelegramReply>();
        _builder.LastPeriodsAgo.Should().Be(expectedPeriodsAgo);
    }

    /// <summary>Стаб: поведение задаётся полями до отправки сообщения актору.</summary>
    private sealed class StubReportBuilder : IReportBuilder
    {
        public ReportResult? Report { get; set; }
        public ExportResult? Export { get; set; }
        public Exception? Throw { get; set; }
        public int? LastPeriodsAgo { get; private set; }

        public Task<ReportResult> BuildAsync(Guid userId, int periodsAgo, CancellationToken ct)
        {
            LastPeriodsAgo = periodsAgo;
            return Throw is not null ? Task.FromException<ReportResult>(Throw) : Task.FromResult(Report!);
        }

        public Task<ReportResult> BuildStatsAsync(Guid userId, int periodsAgo, CancellationToken ct)
        {
            LastPeriodsAgo = periodsAgo;
            return Throw is not null ? Task.FromException<ReportResult>(Throw) : Task.FromResult(Report!);
        }

        public Task<ExportResult> ExportExpensesAsync(Guid userId, int periodsAgo, CancellationToken ct)
        {
            LastPeriodsAgo = periodsAgo;
            return Throw is not null ? Task.FromException<ExportResult>(Throw) : Task.FromResult(Export!);
        }
    }
}
