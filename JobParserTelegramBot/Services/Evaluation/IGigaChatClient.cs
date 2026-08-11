namespace JobParserTelegramBot.Services.Evaluation;

public interface IGigaChatClient
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default);
}
