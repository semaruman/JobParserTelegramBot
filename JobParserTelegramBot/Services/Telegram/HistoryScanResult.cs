namespace JobParserTelegramBot.Services.Telegram;

public sealed class HistoryScanResult
{
    public int ChatsScanned { get; init; }
    public int MessagesChecked { get; init; }
    public int VacanciesFound { get; init; }
    public int CardsSent { get; init; }
    public int Errors { get; init; }
}
