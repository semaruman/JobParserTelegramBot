using JobParserTelegramBot.Data;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TL;
using Message = TL.Message;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class VacancyMonitorHostedService : BackgroundService
{
    private readonly ITelegramSessionService _session;
    private readonly IChatRepository _chatRepository;
    private readonly IVacancyProcessingService _processing;
    private readonly CommandHandler _commandHandler;
    private readonly IProcessedMessageStore _processedStore;
    private readonly ILogger<VacancyMonitorHostedService> _logger;

    public VacancyMonitorHostedService(
        ITelegramSessionService session,
        IChatRepository chatRepository,
        IVacancyProcessingService processing,
        CommandHandler commandHandler,
        IProcessedMessageStore processedStore,
        ILogger<VacancyMonitorHostedService> logger)
    {
        _session = session;
        _chatRepository = chatRepository;
        _processing = processing;
        _commandHandler = commandHandler;
        _processedStore = processedStore;
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
        if (update is not UpdateNewMessage { message: Message message })
        {
            return;
        }

        if (_commandHandler.IsCommandFromSelf(message))
        {
            await _commandHandler.HandleAsync(message, cancellationToken);
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

        await _processing.TryProcessVacancyAsync(message, source, cancellationToken);
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

    private static long? GetChatId(Message message) => message.peer_id switch
    {
        PeerChannel channel => channel.channel_id,
        PeerChat chat => chat.chat_id,
        PeerUser user => user.user_id,
        _ => null
    };
}
