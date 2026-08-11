namespace JobParserTelegramBot.Configuration;

public sealed class EvaluationOptions
{
    public const string SectionName = "Evaluation";

    public int MinScoreToNotify { get; set; } = 60;
    public string SystemPromptPath { get; set; } = "Prompts/vacancy-evaluator-system.txt";
    public string ChatsPath { get; set; } = "data/chats.json";
    public string ProcessedPath { get; set; } = "data/processed.json";
}
