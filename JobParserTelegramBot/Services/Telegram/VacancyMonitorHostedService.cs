using JobParserTelegramBot.Data;
using JobParserTelegramBot.Models;
using JobParserTelegramBot.Services.Evaluation;
using JobParserTelegramBot.Services.Filtering;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TL;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class VacancyMonitorHostedService : BackgroundService
{
    private readonly ITelegramSessionService _session;
    private readonly IChatRepository _chatRepository;
    private readonly IProcessedMessageStore _processedStore;
    private readonly VacancyHeuristicFilter _heuristicFilter;
    private readonly IVacancyEvaluationService _evaluationService;
    private readonly INotificationService _notificationService;
    private readonly CommandHandler _commandHandler;
    private readonly ILogger<VacancyMonitorHostedService> _logger;

    public VacancyMonitorHostedService(
        ITelegramSessionService session,
        IChatRepository chatRepository,
        IProcessedMessageStore processedStore,
        VacancyHeuristicFilter heuristicFilter,
        IVacancyEvaluationService evaluationService,
        INotificationService notificationService,
        CommandHandler commandHandler,
        ILogger<VacancyMonitorHostedService> logger)
    {
        _session = session;
        _chatRepository = chatRepository;
        _processedStore = processedStore;
        _heuristicFilter = heuristicFilter;
        _evaluationService = evaluationService;
        _notificationService = notificationService;
        _commandHandler = commandHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _session.SetUpdateHandler(async update =>
        {
            try
            {
                await OnUpdateAsync(update, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling Telegram update");
            }
        });

        await _session.StartAsync(stoppingToken);
        _logger.LogInformation("Vacancy monitor started. Waiting for messages...");
        _logger.LogInformation("Manage chats via Saved Messages: /addchat @channel, /listchats, /help");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _processedStore.CleanupAsync(TimeSpan.FromDays(30), stoppingToken);
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _session.StopAsync(CancellationToken.None);
        }
    }

    private async Task OnUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        // UpdateNewMessage also covers channel posts.
        if (update is not UpdateNewMessage { message: Message message })
        {
            return;
        }

        await ProcessMessageAsync(message, cancellationToken);
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken cancellationToken)
    {
        if (_commandHandler.IsCommandFromSelf(message))
        {
            await _commandHandler.HandleAsync(message, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(message.message))
        {
            return;
        }

        var self = _session.Self;
        if (self is not null && message.from_id is PeerUser fromUser && fromUser.user_id == self.id)
        {
            return;
        }

        var chatId = GetChatId(message);
        if (chatId is null)
        {
            return;
        }

        var source = await ResolveSourceAsync(chatId.Value, cancellationToken);
        if (source is null || !source.Enabled)
        {
            return;
        }

        var isNew = await _processedStore.TryMarkProcessedAsync(chatId.Value, message.id, cancellationToken);
        if (!isNew)
        {
            return;
        }

        if (!_heuristicFilter.LooksLikeVacancy(message.message))
        {
            _logger.LogDebug("Skipped by heuristic filter: chat={ChatId} msg={MessageId}", chatId, message.id);
            return;
        }

        var vacancy = BuildVacancyMessage(message, source, chatId.Value);
        _logger.LogInformation("Evaluating vacancy from {ChatTitle} (msg {MessageId})", vacancy.ChatTitle, vacancy.MessageId);

        VacancyEvaluation evaluation;
        try
        {
            evaluation = await _evaluationService.EvaluateAsync(vacancy.Text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Evaluation failed for chat {ChatId} message {MessageId}", chatId, message.id);
            return;
        }

        if (!evaluation.ShouldNotify)
        {
            _logger.LogInformation(
                "Vacancy skipped after evaluation: score={Score}, isVacancy={IsVacancy}",
                evaluation.Score, evaluation.IsVacancy);
            return;
        }

        await _notificationService.SendVacancyCardAsync(vacancy, evaluation, cancellationToken);
    }

    private async Task<ChatSource?> ResolveSourceAsync(long chatId, CancellationToken cancellationToken)
    {
        var source = await _chatRepository.FindByIdAsync(chatId, cancellationToken);
        if (source is not null)
        {
            return source;
        }

        if (_session.Manager?.Chats.TryGetValue(chatId, out var chatBase) == true &&
            chatBase is Channel { username: { Length: > 0 } uname })
        {
            return await _chatRepository.FindByUsernameAsync(uname, cancellationToken);
        }

        return null;
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
