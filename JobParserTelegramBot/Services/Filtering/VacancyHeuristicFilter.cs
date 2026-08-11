namespace JobParserTelegramBot.Services.Filtering;

public sealed class VacancyHeuristicFilter
{
    private static readonly string[] Keywords =
    [
        "вакансия", "вакансии", "ищем", "требуется", "нужен", "нужна", "нужны",
        "разработчик", "developer", "backend", "back-end", ".net", "c#", "csharp",
        "asp.net", "senior", "middle", "junior", "зарплата", "з/п", "оклад",
        "удалён", "удален", "remote", "full-time", "fulltime", "офис",
        "требования", "обязанности", "стек", "experience", "required", "salary",
        "ищут", "нанимаем", "открыта позиция", "job", "hiring", "отклик"
    ];

    public bool LooksLikeVacancy(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 40)
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        var hits = Keywords.Count(k => lower.Contains(k, StringComparison.Ordinal));
        return hits >= 2 || (hits >= 1 && text.Length >= 200);
    }
}
