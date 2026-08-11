namespace JobParserTelegramBot.Models;

public sealed class VacancyMessage
{
    public long ChatId { get; init; }
    public int MessageId { get; init; }
    public string ChatTitle { get; init; } = string.Empty;
    public string? ChatUsername { get; init; }
    public string Text { get; init; } = string.Empty;
    public long? AuthorId { get; init; }
    public string? AuthorUsername { get; init; }
    public string? AuthorDisplayName { get; init; }
    public bool IsChannelPost { get; init; }
}
