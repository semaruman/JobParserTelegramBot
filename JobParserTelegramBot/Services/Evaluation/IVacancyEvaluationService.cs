using JobParserTelegramBot.Models;

namespace JobParserTelegramBot.Services.Evaluation;

public interface IVacancyEvaluationService
{
    Task<VacancyEvaluation> EvaluateAsync(string vacancyText, CancellationToken cancellationToken = default);
}
