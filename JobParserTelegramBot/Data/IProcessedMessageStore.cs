namespace JobParserTelegramBot.Data;

public interface IProcessedMessageStore
{
    Task<bool> TryMarkProcessedAsync(long chatId, int messageId, CancellationToken cancellationToken = default);
    Task CleanupAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}
