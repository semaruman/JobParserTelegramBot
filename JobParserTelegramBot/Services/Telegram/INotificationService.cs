using JobParserTelegramBot.Models;

namespace JobParserTelegramBot.Services.Telegram;

public interface INotificationService
{
    Task SendVacancyCardAsync(VacancyMessage vacancy, VacancyEvaluation evaluation, CancellationToken cancellationToken = default);
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);
}
