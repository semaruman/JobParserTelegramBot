using JobParserTelegramBot.Models;
using TL;
using Message = TL.Message;

namespace JobParserTelegramBot.Services.Telegram;

public interface IVacancyProcessingService
{
    /// <summary>
    /// Processes a chat message as a potential vacancy.
    /// Returns true if a notification card was sent.
    /// </summary>
    Task<bool> TryProcessVacancyAsync(Message message, ChatSource source, CancellationToken cancellationToken = default);
}
