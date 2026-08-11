namespace JobParserTelegramBot.Models;

public sealed class VacancyEvaluation
{
    public int Score { get; init; }
    public string SuitabilityEmoji { get; init; } = string.Empty;
    public string ShouldApplyVerdict { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string FullAnalysisText { get; init; } = string.Empty;
    public bool IsVacancy { get; init; }
    public bool ShouldNotify { get; init; }
}
