using System.Text;
using Microsoft.Extensions.Logging;
using TL;
using Message = TL.Message;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class CommandHandler
{
    private readonly IChatManagementService _chatManagement;
    private readonly ITelegramSessionService _session;
    private readonly INotificationService _notifications;
    private readonly ILogger<CommandHandler> _logger;

    public CommandHandler(
        IChatManagementService chatManagement,
        ITelegramSessionService session,
        INotificationService notifications,
        ILogger<CommandHandler> logger)
    {
        _chatManagement = chatManagement;
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

                    var added = await _chatManagement.AddAsync(parts[1], cancellationToken);
                    await _notifications.SendTextAsync(
                        $"Добавлено: {added.Title} (@{added.Username ?? "-"}, id={added.Id})",
                        cancellationToken);
                    break;
                case "/removechat":
                    if (parts.Length < 2)
                    {
                        await _notifications.SendTextAsync("Использование: /removechat @username или /removechat <chatId>", cancellationToken);
                        break;
                    }

                    var removed = await _chatManagement.RemoveAsync(parts[1], cancellationToken);
                    await _notifications.SendTextAsync(
                        removed ? "Чат удалён из списка." : "Чат не найден в списке.",
                        cancellationToken);
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
        var chats = await _chatManagement.ListAsync(cancellationToken);
        if (chats.Count == 0)
        {
            await _notifications.SendTextAsync("Список чатов пуст. Добавь через GUI или /addchat @username", cancellationToken);
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

    private const string HelpText =
        """
        Команды (пиши себе в Saved Messages):

        /addchat @username — добавить чат/канал
        /addchat <chatId> — добавить по id
        /removechat @username|<chatId> — удалить
        /listchats — список чатов
        /help — справка

        Каналы также можно добавлять в окне приложения.
        """;
}
