using JobParserTelegramBot.Data;
using JobParserTelegramBot.Models;
using JobParserTelegramBot.Services.Evaluation;
using JobParserTelegramBot.Services.Filtering;
using Microsoft.Extensions.Logging;
using TL;
using Message = TL.Message;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class VacancyProcessingService : IVacancyProcessingService
{
    private readonly ITelegramSessionService _session;
    private readonly IProcessedMessageStore _processedStore;
    private readonly VacancyHeuristicFilter _heuristicFilter;
    private readonly IVacancyEvaluationService _evaluationService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<VacancyProcessingService> _logger;

    public VacancyProcessingService(
        ITelegramSessionService session,
        IProcessedMessageStore processedStore,
        VacancyHeuristicFilter heuristicFilter,
        IVacancyEvaluationService evaluationService,
        INotificationService notificationService,
        ILogger<VacancyProcessingService> logger)
    {
        _session = session;
        _processedStore = processedStore;
        _heuristicFilter = heuristicFilter;
        _evaluationService = evaluationService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<bool> TryProcessVacancyAsync(Message message, ChatSource source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.message) || !source.Enabled)
        {
            return false;
        }

        var chatId = GetChatId(message) ?? source.Id;

        var isNew = await _processedStore.TryMarkProcessedAsync(chatId, message.id, cancellationToken);
        if (!isNew)
        {
            return false;
        }

        if (!_heuristicFilter.LooksLikeVacancy(message.message))
        {
            _logger.LogDebug("Skipped by heuristic filter: chat={ChatId} msg={MessageId}", chatId, message.id);
            return false;
        }

        var vacancy = BuildVacancyMessage(message, source, chatId);
        _logger.LogInformation("Evaluating vacancy from {ChatTitle} (msg {MessageId})", vacancy.ChatTitle, vacancy.MessageId);

        VacancyEvaluation evaluation;
        try
        {
            evaluation = await _evaluationService.EvaluateAsync(vacancy.Text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Evaluation failed for chat {ChatId} message {MessageId}", chatId, message.id);
            return false;
        }

        if (!evaluation.ShouldNotify)
        {
            _logger.LogInformation(
                "Vacancy skipped after evaluation: score={Score}, isVacancy={IsVacancy}",
                evaluation.Score, evaluation.IsVacancy);
            return false;
        }

        await _notificationService.SendVacancyCardAsync(vacancy, evaluation, cancellationToken);
        return true;
    }

    private VacancyMessage BuildVacancyMessage(Message message, ChatSource source, long chatId)
    {
        string? authorUsername = null;
        string? authorDisplayName = null;
        long? authorId = null;

        if (message.from_id is PeerUser peerUser &&
            _session.Manager?.Users.TryGetValue(peerUser.user_id, out var user) == true)
        {
            authorId = user.id;
            authorUsername = user.username;
            authorDisplayName = $"{user.first_name} {user.last_name}".Trim();
        }
        else if (message.from_id is PeerChannel peerChannel &&
                 _session.Manager?.Chats.TryGetValue(peerChannel.channel_id, out var channelChat) == true &&
                 channelChat is Channel channel)
        {
            authorId = channel.ID;
            authorUsername = channel.username;
            authorDisplayName = channel.Title;
        }

        return new VacancyMessage
        {
            ChatId = chatId,
            MessageId = message.id,
            ChatTitle = source.Title,
            ChatUsername = source.Username,
            Text = message.message,
            AuthorId = authorId,
            AuthorUsername = authorUsername,
            AuthorDisplayName = authorDisplayName,
            IsChannelPost = message.peer_id is PeerChannel
        };
    }

    private static long? GetChatId(Message message) => message.peer_id switch
    {
        PeerChannel channel => channel.channel_id,
        PeerChat chat => chat.chat_id,
        PeerUser user => user.user_id,
        _ => null
    };
}
