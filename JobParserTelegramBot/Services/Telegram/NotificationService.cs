using System.Text;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Logging;
using TL;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class NotificationService : INotificationService
{
    private const int TelegramMessageLimit = 4000;

    private readonly ITelegramSessionService _session;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ITelegramSessionService session, ILogger<NotificationService> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async Task SendVacancyCardAsync(VacancyMessage vacancy, VacancyEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        var peer = InputPeer.Self;
        var writeUrl = BuildWriteUrl(vacancy);
        var originalUrl = BuildOriginalMessageUrl(vacancy);

        var card = new StringBuilder();
        card.AppendLine($"💼 Вакансия из: {vacancy.ChatTitle}");
        card.AppendLine($"⭐ Оценка: {evaluation.Score}/100 {evaluation.SuitabilityEmoji}");
        if (!string.IsNullOrWhiteSpace(evaluation.ShouldApplyVerdict))
        {
            card.AppendLine($"📌 {evaluation.ShouldApplyVerdict}");
        }

        card.AppendLine();
        if (!string.IsNullOrWhiteSpace(evaluation.Summary))
        {
            card.AppendLine(Truncate(evaluation.Summary, 900));
            card.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(originalUrl))
        {
            card.AppendLine($"🔗 Оригинал: {originalUrl}");
        }

        ReplyMarkup? markup = null;
        if (!string.IsNullOrWhiteSpace(writeUrl))
        {
            markup = new ReplyInlineMarkup
            {
                rows =
                [
                    new KeyboardButtonRow
                    {
                        buttons = [new KeyboardButtonUrl { text = "Написать", url = writeUrl }]
                    }
                ]
            };
        }

        await SendAsync(peer, Truncate(card.ToString(), TelegramMessageLimit), markup);

        var full = Truncate(evaluation.FullAnalysisText, TelegramMessageLimit);
        if (!string.IsNullOrWhiteSpace(full))
        {
            await SendAsync(peer, "📋 Полный разбор:\n\n" + full, null);
        }

        _logger.LogInformation("Vacancy card sent for chat {ChatTitle}, message {MessageId}", vacancy.ChatTitle, vacancy.MessageId);
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        await SendAsync(InputPeer.Self, Truncate(text, TelegramMessageLimit), null);
    }

    private async Task SendAsync(InputPeer peer, string text, ReplyMarkup? markup)
    {
        await _session.Client.Messages_SendMessage(
            peer: peer,
            message: text,
            random_id: WTelegram.Helpers.RandomLong(),
            reply_markup: markup);
    }

    private static string? BuildWriteUrl(VacancyMessage vacancy)
    {
        if (!string.IsNullOrWhiteSpace(vacancy.AuthorUsername))
        {
            return $"https://t.me/{vacancy.AuthorUsername.TrimStart('@')}";
        }

        return BuildOriginalMessageUrl(vacancy);
    }

    private static string? BuildOriginalMessageUrl(VacancyMessage vacancy)
    {
        if (!string.IsNullOrWhiteSpace(vacancy.ChatUsername))
        {
            return $"https://t.me/{vacancy.ChatUsername.TrimStart('@')}/{vacancy.MessageId}";
        }

        var abs = Math.Abs(vacancy.ChatId).ToString();
        var channelPart = abs.StartsWith("100", StringComparison.Ordinal) && abs.Length > 3
            ? abs[3..]
            : abs;

        return $"https://t.me/c/{channelPart}/{vacancy.MessageId}";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 1)] + "…";
    }
}
