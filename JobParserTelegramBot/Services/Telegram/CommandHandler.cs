using System.Text;
using JobParserTelegramBot.Data;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Logging;
using TL;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class CommandHandler
{
    private readonly IChatRepository _chatRepository;
    private readonly ITelegramSessionService _session;
    private readonly INotificationService _notifications;
    private readonly ILogger<CommandHandler> _logger;

    public CommandHandler(
        IChatRepository chatRepository,
        ITelegramSessionService session,
        INotificationService notifications,
        ILogger<CommandHandler> logger)
    {
        _chatRepository = chatRepository;
        _session = session;
        _notifications = notifications;
        _logger = logger;
    }

    public bool IsCommandFromSelf(MessageBase message)
    {
        var self = _session.Self;
        if (self is null || message is not Message msg)
        {
            return false;
        }

        if (msg.peer_id is not PeerUser peerUser || peerUser.user_id != self.id)
        {
            return false;
        }

        return msg.message.StartsWith('/');
    }

    public async Task HandleAsync(Message message, CancellationToken cancellationToken = default)
    {
        var text = message.message.Trim();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0].Split('@')[0].ToLowerInvariant();

        try
        {
            switch (command)
            {
                case "/help":
                    await _notifications.SendTextAsync(HelpText, cancellationToken);
                    break;
                case "/listchats":
                    await ListChatsAsync(cancellationToken);
                    break;
                case "/addchat":
                    if (parts.Length < 2)
                    {
                        await _notifications.SendTextAsync("Использование: /addchat @username или /addchat <chatId>", cancellationToken);
                        break;
                    }

                    await AddChatAsync(parts[1], cancellationToken);
                    break;
                case "/removechat":
                    if (parts.Length < 2)
                    {
                        await _notifications.SendTextAsync("Использование: /removechat @username или /removechat <chatId>", cancellationToken);
                        break;
                    }

                    await RemoveChatAsync(parts[1], cancellationToken);
                    break;
                default:
                    await _notifications.SendTextAsync("Неизвестная команда. /help", cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command failed: {Command}", text);
            await _notifications.SendTextAsync($"Ошибка: {ex.Message}", cancellationToken);
        }
    }

    private async Task ListChatsAsync(CancellationToken cancellationToken)
    {
        var chats = await _chatRepository.GetAllAsync(cancellationToken);
        if (chats.Count == 0)
        {
            await _notifications.SendTextAsync("Список чатов пуст. Добавь через /addchat @username", cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Чаты для парсинга:");
        foreach (var chat in chats)
        {
            var status = chat.Enabled ? "✅" : "⏸";
            var username = string.IsNullOrEmpty(chat.Username) ? "-" : "@" + chat.Username;
            sb.AppendLine($"{status} {chat.Title} | {username} | id={chat.Id}");
        }

        await _notifications.SendTextAsync(sb.ToString(), cancellationToken);
    }

    private async Task AddChatAsync(string raw, CancellationToken cancellationToken)
    {
        ChatSource chat;
        if (long.TryParse(raw, out var chatId))
        {
            var peer = await _session.ResolvePeerAsync(chatId, null, cancellationToken);
            if (peer is null)
            {
                await _notifications.SendTextAsync(
                    $"Не удалось найти чат с id={chatId}. Убедись, что аккаунт уже в этом чате.",
                    cancellationToken);
                return;
            }

            chat = await BuildChatSourceAsync(chatId);
        }
        else
        {
            var username = raw.Trim().TrimStart('@');
            var resolved = await _session.Client.Contacts_ResolveUsername(username);
            TelegramSessionService.MergePeers(resolved.users, resolved.chats, _session.Manager);

            if (resolved.Chat is not ChatBase chatBase)
            {
                await _notifications.SendTextAsync($"@{username} не является группой/каналом.", cancellationToken);
                return;
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
        await _notifications.SendTextAsync($"Добавлено: {chat.Title} (@{chat.Username ?? "-"}, id={chat.Id})", cancellationToken);
    }

    private async Task RemoveChatAsync(string raw, CancellationToken cancellationToken)
    {
        bool removed;
        if (long.TryParse(raw, out var chatId))
        {
            removed = await _chatRepository.RemoveAsync(chatId, cancellationToken);
        }
        else
        {
            removed = await _chatRepository.RemoveByUsernameAsync(raw, cancellationToken);
        }

        await _notifications.SendTextAsync(removed ? "Чат удалён из списка." : "Чат не найден в списке.", cancellationToken);
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
        catch
        {
            // keep fallback
        }

        return new ChatSource { Id = id, Title = title, Username = username, Enabled = true };
    }

    private const string HelpText =
        """
        Команды (пиши себе в Saved Messages):

        /addchat @username — добавить чат/канал
        /addchat <chatId> — добавить по id
        /removechat @username|<chatId> — удалить
        /listchats — список чатов
        /help — справка
        """;
}
