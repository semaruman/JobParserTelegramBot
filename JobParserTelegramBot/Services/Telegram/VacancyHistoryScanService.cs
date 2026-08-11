using JobParserTelegramBot.Data;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Logging;
using TL;
using Message = TL.Message;

namespace JobParserTelegramBot.Services.Telegram;

public interface IVacancyHistoryScanService
{
    Task<HistoryScanResult> ScanLastDayAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class VacancyHistoryScanService : IVacancyHistoryScanService
{
    private readonly ITelegramSessionService _session;
    private readonly IChatRepository _chatRepository;
    private readonly IVacancyProcessingService _processing;
    private readonly ILogger<VacancyHistoryScanService> _logger;

    public VacancyHistoryScanService(
        ITelegramSessionService session,
        IChatRepository chatRepository,
        IVacancyProcessingService processing,
        ILogger<VacancyHistoryScanService> logger)
    {
        _session = session;
        _chatRepository = chatRepository;
        _processing = processing;
        _logger = logger;
    }

    public async Task<HistoryScanResult> ScanLastDayAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_session.Self is null)
        {
            throw new InvalidOperationException("Telegram ещё не залогинен.");
        }

        var sinceUtc = DateTime.UtcNow.AddDays(-1);
        var chats = (await _chatRepository.GetAllAsync(cancellationToken))
            .Where(c => c.Enabled)
            .ToList();

        if (chats.Count == 0)
        {
            throw new InvalidOperationException("Нет включённых каналов. Добавь хотя бы один.");
        }

        var messagesChecked = 0;
        var vacanciesFound = 0;
        var cardsSent = 0;
        var errors = 0;
        var chatsScanned = 0;

        foreach (var chat in chats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Сканирую: {chat.Title}…");

            try
            {
                var peer = await _session.ResolvePeerAsync(chat.Id, chat.Username, cancellationToken);
                if (peer is null)
                {
                    _logger.LogWarning("Cannot resolve peer for chat {Title} ({Id})", chat.Title, chat.Id);
                    errors++;
                    continue;
                }

                chatsScanned++;
                var (checkedCount, foundCount, sentCount) = await ScanChatAsync(peer, chat, sinceUtc, progress, cancellationToken);
                messagesChecked += checkedCount;
                vacanciesFound += foundCount;
                cardsSent += sentCount;

                // small pause between chats to reduce flood risk
                await Task.Delay(400, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "History scan failed for chat {Title}", chat.Title);
                progress?.Report($"Ошибка в «{chat.Title}»: {ex.Message}");
            }
        }

        progress?.Report($"Готово: каналов {chatsScanned}, сообщений {messagesChecked}, карточек {cardsSent}");

        return new HistoryScanResult
        {
            ChatsScanned = chatsScanned,
            MessagesChecked = messagesChecked,
            VacanciesFound = vacanciesFound,
            CardsSent = cardsSent,
            Errors = errors
        };
    }

    private async Task<(int Checked, int Found, int Sent)> ScanChatAsync(
        InputPeer peer,
        ChatSource chat,
        DateTime sinceUtc,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var checkedCount = 0;
        var foundCount = 0;
        var sentCount = 0;
        var offsetId = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var history = await _session.Client.Messages_GetHistory(peer, offset_id: offsetId, limit: 100);
            if (_session.Manager is not null)
            {
                history.CollectUsersChats(_session.Manager.Users, _session.Manager.Chats);
            }

            if (history.Messages.Length == 0)
            {
                break;
            }

            var reachedOlder = false;
            foreach (var msgBase in history.Messages)
            {
                if (msgBase is not Message message)
                {
                    continue;
                }

                var msgDate = message.date.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(message.date, DateTimeKind.Utc)
                    : message.date.ToUniversalTime();

                if (msgDate < sinceUtc)
                {
                    reachedOlder = true;
                    continue;
                }

                checkedCount++;
                if (string.IsNullOrWhiteSpace(message.message))
                {
                    continue;
                }

                var wasNotified = await _processing.TryProcessVacancyAsync(message, chat, cancellationToken);
                if (wasNotified)
                {
                    foundCount++;
                    sentCount++;
                    progress?.Report($"Отправлена карточка из «{chat.Title}»");
                }
            }

            offsetId = history.Messages[^1].ID;
            if (reachedOlder || history.Messages.Length < 100)
            {
                break;
            }

            await Task.Delay(250, cancellationToken);
        }

        return (checkedCount, foundCount, sentCount);
    }
}
