using JobParserTelegramBot.Data;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Logging;
using TL;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class ChatManagementService : IChatManagementService
{
    private readonly IChatRepository _chatRepository;
    private readonly ITelegramSessionService _session;
    private readonly ILogger<ChatManagementService> _logger;

    public ChatManagementService(
        IChatRepository chatRepository,
        ITelegramSessionService session,
        ILogger<ChatManagementService> logger)
    {
        _chatRepository = chatRepository;
        _session = session;
        _logger = logger;
    }

    public Task<IReadOnlyList<ChatSource>> ListAsync(CancellationToken cancellationToken = default) =>
        _chatRepository.GetAllAsync(cancellationToken);

    public async Task<ChatSource> AddAsync(string usernameOrId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usernameOrId))
        {
            throw new ArgumentException("Укажи @username или id чата.", nameof(usernameOrId));
        }

        var raw = usernameOrId.Trim();
        ChatSource chat;

        if (long.TryParse(raw, out var chatId))
        {
            var peer = await _session.ResolvePeerAsync(chatId, null, cancellationToken);
            if (peer is null)
            {
                throw new InvalidOperationException(
                    $"Не удалось найти чат с id={chatId}. Убедись, что аккаунт уже в этом чате.");
            }

            chat = await BuildChatSourceAsync(chatId);
        }
        else
        {
            if (_session.Self is null)
            {
                throw new InvalidOperationException("Telegram-сессия ещё не готова. Подожди логин.");
            }

            var username = raw.TrimStart('@');
            var resolved = await _session.Client.Contacts_ResolveUsername(username);
            TelegramSessionService.MergePeers(resolved.users, resolved.chats, _session.Manager);

            if (resolved.Chat is not ChatBase chatBase)
            {
                throw new InvalidOperationException($"@{username} не является группой/каналом.");
            }

            chat = new ChatSource
            {
                Id = chatBase.ID,
                Title = chatBase.Title ?? username,
                Username = username,
                Enabled = true
            };
        }

        await _chatRepository.AddOrUpdateAsync(chat, cancellationToken);
        _logger.LogInformation("Chat added: {Title} (@{Username}, id={Id})", chat.Title, chat.Username, chat.Id);
        return chat;
    }

    public async Task<bool> RemoveAsync(string usernameOrId, CancellationToken cancellationToken = default)
    {
        var raw = usernameOrId.Trim();
        if (long.TryParse(raw, out var chatId))
        {
            return await _chatRepository.RemoveAsync(chatId, cancellationToken);
        }

        return await _chatRepository.RemoveByUsernameAsync(raw, cancellationToken);
    }

    public Task<bool> RemoveByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _chatRepository.RemoveAsync(id, cancellationToken);

    public async Task SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default)
    {
        var chat = await _chatRepository.FindByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Чат id={id} не найден.");

        chat.Enabled = enabled;
        await _chatRepository.AddOrUpdateAsync(chat, cancellationToken);
    }

    private async Task<ChatSource> BuildChatSourceAsync(long fallbackId)
    {
        string title = $"Chat {fallbackId}";
        string? username = null;
        long id = fallbackId;

        if (_session.Manager?.Chats.TryGetValue(fallbackId, out var known) == true)
        {
            title = known.Title ?? title;
            id = known.ID;
            username = known is Channel channel ? channel.username : null;
            return new ChatSource { Id = id, Title = title, Username = username, Enabled = true };
        }

        try
        {
            var all = await _session.Client.Messages_GetAllChats();
            TelegramSessionService.MergePeers(null, all.chats, _session.Manager);
            if (all.chats.TryGetValue(fallbackId, out var found))
            {
                title = found.Title ?? title;
                id = found.ID;
                username = found is Channel ch ? ch.username : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enrich chat metadata for id={ChatId}", fallbackId);
        }

        return new ChatSource { Id = id, Title = title, Username = username, Enabled = true };
    }
}
