using System.Text.RegularExpressions;
using JobParserTelegramBot.Configuration;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobParserTelegramBot.Services.Evaluation;

public sealed partial class VacancyEvaluationService : IVacancyEvaluationService
{
    private readonly IGigaChatClient _gigaChatClient;
    private readonly EvaluationOptions _options;
    private readonly ILogger<VacancyEvaluationService> _logger;
    private readonly Lazy<string> _systemPrompt;

    public VacancyEvaluationService(
        IGigaChatClient gigaChatClient,
        IOptions<EvaluationOptions> options,
        ILogger<VacancyEvaluationService> logger)
    {
        _gigaChatClient = gigaChatClient;
        _options = options.Value;
        _logger = logger;
        _systemPrompt = new Lazy<string>(LoadSystemPrompt);
    }

    public async Task<VacancyEvaluation> EvaluateAsync(string vacancyText, CancellationToken cancellationToken = default)
    {
        var analysis = await _gigaChatClient.CompleteAsync(_systemPrompt.Value, vacancyText, cancellationToken);
        return Parse(analysis);
    }

    private VacancyEvaluation Parse(string analysis)
    {
        var isVacancy = !analysis.Contains("НЕ ВАКАНСИЯ", StringComparison.OrdinalIgnoreCase);

        var scoreMatch = ScoreRegex().Match(analysis);
        var score = scoreMatch.Success && int.TryParse(scoreMatch.Groups[1].Value, out var parsedScore)
            ? Math.Clamp(parsedScore, 0, 100)
            : 0;

        var emoji = "🔴";
        if (analysis.Contains("🟢", StringComparison.Ordinal))
        {
            emoji = "🟢";
        }
        else if (analysis.Contains("🟡", StringComparison.Ordinal))
        {
            emoji = "🟡";
        }
        else if (analysis.Contains("🟠", StringComparison.Ordinal))
        {
            emoji = "🟠";
        }

        var verdict = ExtractVerdict(analysis);
        var summary = ExtractSummary(analysis, verdict);
        var shouldNotify = isVacancy && (score >= _options.MinScoreToNotify || emoji is "🟢" or "🟡");

        _logger.LogInformation(
            "Vacancy evaluated: IsVacancy={IsVacancy}, Score={Score}, Emoji={Emoji}, Notify={Notify}",
            isVacancy, score, emoji, shouldNotify);

        return new VacancyEvaluation
        {
            Score = score,
            SuitabilityEmoji = emoji,
            ShouldApplyVerdict = verdict,
            Summary = summary,
            FullAnalysisText = analysis,
            IsVacancy = isVacancy,
            ShouldNotify = shouldNotify
        };
    }

    private static string ExtractVerdict(string analysis)
    {
        var section = ExtractSection(analysis, "Стоит ли откликаться");
        if (string.IsNullOrWhiteSpace(section))
        {
            return string.Empty;
        }

        foreach (var marker in new[] { "✅ Однозначно да", "👍 Да", "🤔 Можно попробовать", "👎 Скорее нет", "❌ Нет" })
        {
            if (section.Contains(marker, StringComparison.Ordinal))
            {
                return marker;
            }
        }

        return section.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string ExtractSummary(string analysis, string verdict)
    {
        var why = ExtractSection(analysis, "Почему такая оценка");
        if (string.IsNullOrWhiteSpace(why))
        {
            why = ExtractSection(analysis, "Стоит ли откликаться");
        }

        var lines = why
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#'))
            .Take(4);

        var body = string.Join("\n", lines);
        return string.IsNullOrWhiteSpace(verdict) ? body : $"{verdict}\n\n{body}".Trim();
    }

    private static string ExtractSection(string analysis, string headingHint)
    {
        var lines = analysis.Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(headingHint, StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
        {
            return string.Empty;
        }

        var buffer = new List<string>();
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("# ") || line.StartsWith("## ") || line is "---")
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                buffer.Add(line);
            }
        }

        return string.Join("\n", buffer);
    }

    private string LoadSystemPrompt()
    {
        var path = Path.GetFullPath(_options.SystemPromptPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"System prompt file not found: {path}");
        }

        return File.ReadAllText(path);
    }

    [GeneratedRegex(@"Совпадение:\s*(\d+)\s*/\s*100", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScoreRegex();
}
