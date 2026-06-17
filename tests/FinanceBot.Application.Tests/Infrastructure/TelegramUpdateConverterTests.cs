using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Infrastructure.Telegram;
using FluentAssertions;
using Telegram.Bot.Types;
using Xunit;

namespace FinanceBot.Application.Tests.Infrastructure;

public sealed class TelegramUpdateConverterTests
{
    private static User Sender => new() { Id = 42, FirstName = "T", Username = "t" };
    private static Chat Chat => new() { Id = 100 };

    [Fact]
    public void Photo_message_converts_to_file_photo_with_largest_size()
    {
        var update = new Update
        {
            Id = 1,
            Message = new Message
            {
                Chat = Chat,
                From = Sender,
                Date = DateTime.UtcNow,
                Caption = "выписка",
                Photo = [new PhotoSize { FileId = "small" }, new PhotoSize { FileId = "big" }]
            }
        };

        var (text, callback, file) = TelegramUpdateConverter.TryConvert(update);

        text.Should().BeNull();
        callback.Should().BeNull();
        file.Should().NotBeNull();
        file!.Kind.Should().Be(FileKind.Photo);
        file.FileId.Should().Be("big");
        file.MimeType.Should().Be("image/jpeg");
        file.Caption.Should().Be("выписка");
        file.ChatId.Should().Be(100);
        file.TelegramId.Should().Be(42);
    }

    [Fact]
    public void Document_message_converts_to_file_document()
    {
        var update = new Update
        {
            Id = 2,
            Message = new Message
            {
                Chat = Chat,
                From = Sender,
                Date = DateTime.UtcNow,
                Document = new Document { FileId = "doc1", MimeType = "text/csv", FileName = "statement.csv" }
            }
        };

        var (_, _, file) = TelegramUpdateConverter.TryConvert(update);

        file.Should().NotBeNull();
        file!.Kind.Should().Be(FileKind.Document);
        file.FileId.Should().Be("doc1");
        file.MimeType.Should().Be("text/csv");
    }

    [Fact]
    public void Text_message_converts_to_update_not_file()
    {
        var update = new Update
        {
            Id = 3,
            Message = new Message { Chat = Chat, From = Sender, Date = DateTime.UtcNow, Text = "/start" }
        };

        var (text, _, file) = TelegramUpdateConverter.TryConvert(update);

        file.Should().BeNull();
        text.Should().NotBeNull();
        text!.Text.Should().Be("/start");
    }
}
