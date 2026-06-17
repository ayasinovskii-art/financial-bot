namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class ImportCommandHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Import;

    public void Execute(TelegramCommandContext ctx)
        => ctx.Reply(TelegramReplies.CsvImportInstruction());
}
