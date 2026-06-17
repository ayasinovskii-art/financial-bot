using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.Claude;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceBot.Application.Tests.Infrastructure;

public sealed class ClaudeStatementExtractorTests
{
    private static readonly byte[] FakeImage = [1, 2, 3, 4];

    private sealed class StubClaudeClient(ClaudeResponse response) : IClaudeClient
    {
        public ClaudeRequest? LastRequest { get; private set; }

        public Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(response with { CorrelationId = request.CorrelationId });
        }
    }

    private static ClaudeResponse Ok(string content)
        => new(Guid.NewGuid(), content, true, 10, 100, 50, null, null, null, null);

    private static ClaudeResponse Failed()
        => new(Guid.NewGuid(), null, false, 10, null, null, null, null, ClaudeUnavailabilityReason.RateLimited, "429");

    private static ClaudeStatementExtractor Make(ClaudeResponse response, out StubClaudeClient stub)
    {
        stub = new StubClaudeClient(response);
        return new ClaudeStatementExtractor(stub, NullLogger<ClaudeStatementExtractor>.Instance);
    }

    [Fact]
    public async Task Parses_clean_json_array()
    {
        var json = """[{"date":"2026-06-05","amount":750.50,"description":"обед","kind":"expense"},{"date":"2026-06-01","amount":5000,"description":"возврат","kind":"income"}]""";
        var extractor = Make(Ok(json), out _);

        var result = await extractor.ExtractAsync(FakeImage, "image/jpeg", default);

        result.IsSuccess.Should().BeTrue();
        result.Transactions.Should().HaveCount(2);
        result.Transactions[0].Should().Be(new ImportedTransaction(new DateOnly(2026, 6, 5), 750.50m, "обед", TransactionKind.Expense));
        result.Transactions[1].Kind.Should().Be(TransactionKind.Income);
    }

    [Fact]
    public async Task Tolerates_prose_and_code_fences_and_takes_abs_amount()
    {
        var content = "Вот транзакции:\n```json\n[{\"date\":\"2026-06-05\",\"amount\":-750,\"description\":\"обед\",\"kind\":\"expense\"}]\n```\nГотово.";
        var extractor = Make(Ok(content), out _);

        var result = await extractor.ExtractAsync(FakeImage, "image/jpeg", default);

        result.IsSuccess.Should().BeTrue();
        result.Transactions.Should().ContainSingle();
        result.Transactions[0].Amount.Should().Be(750m);
    }

    [Fact]
    public async Task Empty_array_is_success_with_no_transactions()
    {
        var extractor = Make(Ok("[]"), out _);

        var result = await extractor.ExtractAsync(FakeImage, "image/jpeg", default);

        result.IsSuccess.Should().BeTrue();
        result.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_json_content_is_failure()
    {
        var extractor = Make(Ok("Не вижу транзакций на картинке."), out _);

        var result = await extractor.ExtractAsync(FakeImage, "image/jpeg", default);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Claude_failure_propagates_reason()
    {
        var extractor = Make(Failed(), out _);

        var result = await extractor.ExtractAsync(FakeImage, "image/jpeg", default);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ClaudeUnavailabilityReason.RateLimited);
    }

    [Fact]
    public async Task Empty_image_fails_without_calling_claude()
    {
        var extractor = Make(Ok("[]"), out var stub);

        var result = await extractor.ExtractAsync(ReadOnlyMemory<byte>.Empty, "image/jpeg", default);

        result.IsSuccess.Should().BeFalse();
        stub.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Sends_image_block_with_normalized_media_type_and_use_case()
    {
        var extractor = Make(Ok("[]"), out var stub);

        await extractor.ExtractAsync(FakeImage, "application/octet-stream", default);

        stub.LastRequest!.Image.Should().NotBeNull();
        stub.LastRequest.Image!.MediaType.Should().Be("image/jpeg");
        stub.LastRequest.UseCase.Should().Be(ClaudeUseCase.StatementExtraction);
    }
}
