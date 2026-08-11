using JobParserTelegramBot.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TL;
using WTelegram;

namespace JobParserTelegramBot.Services.Telegram;

public sealed class TelegramSessionService : ITelegramSessionService, IAsyncDisposable
{
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramSessionService> _logger;
    private Client? _client;
    private UpdateManager? _manager;
    private Func<Update, Task>? _updateHandler;

    public TelegramSessionService(IOptions<TelegramOptions> options, ILogger<TelegramSessionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Client Client => _client ?? throw new InvalidOperationException("Telegram client is not started.");
    public UpdateManager? Manager => _manager;
    public User? Self { get; private set; }

    public void SetUpdateHandler(Func<Update, Task> handler) => _updateHandler = handler;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            return;
        }

        if (_options.ApiId <= 0 || string.IsNullOrWhiteSpace(_options.ApiHash))
        {
            throw new InvalidOperationException("Telegram ApiId/ApiHash are not configured.");
        }

        var sessionDir = Path.GetDirectoryName(Path.GetFullPath(_options.SessionPath));
        if (!string.IsNullOrEmpty(sessionDir))
        {
            Directory.CreateDirectory(sessionDir);
        }

        _client = new Client(Config);
        _logger.LogInformation("Logging into Telegram as user...");
        Self = await _client.LoginUserIfNeeded();
        _logger.LogInformation("Logged in as {Name} (id={Id})", Self.first_name, Self.id);

        _manager = _client.WithUpdateManager(async update =>
        {
            if (_updateHandler is not null)
            {
                await _updateHandler(update);
            }
        });

        var dialogs = await _client.Messages_GetAllDialogs();
        dialogs.CollectUsersChats(_manager.Users, _manager.Chats);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _client?.Dispose();
        _client = null;
        _manager = null;
        Self = null;
        return Task.CompletedTask;
    }

    public async Task<InputPeer?> ResolvePeerAsync(long chatId, string? username, CancellationToken cancellationToken = default)
    {
        var client = Client;
        var manager = Manager;

        if (manager is not null && manager.Chats.TryGetValue(chatId, out var chat))
        {
            return chat.ToInputPeer();
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var normalized = username.Trim().TrimStart('@');
            var resolved = await client.Contacts_ResolveUsername(normalized);
            MergePeers(resolved.users, resolved.chats, manager);
            if (resolved.Chat is ChatBase resolvedChat)
            {
                return resolvedChat.ToInputPeer();
            }

            if (resolved.User is User resolvedUser)
            {
                return resolvedUser.ToInputPeer();
            }
        }

        if (chatId != 0)
        {
            try
            {
                var all = await client.Messages_GetAllChats();
                MergePeers(null, all.chats, manager);
                if (all.chats.TryGetValue(chatId, out var found))
                {
                    return found.ToInputPeer();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve chat by id {ChatId}", chatId);
            }
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        return ValueTask.CompletedTask;
    }

    internal static void MergePeers(
        IDictionary<long, User>? users,
        IDictionary<long, ChatBase>? chats,
        UpdateManager? manager)
    {
        if (manager is null)
        {
            return;
        }

        if (users is not null)
        {
            foreach (var (id, user) in users)
            {
                manager.Users[id] = user;
            }
        }

        if (chats is not null)
        {
            foreach (var (id, chat) in chats)
            {
                manager.Chats[id] = chat;
            }
        }
    }

    private string? Config(string what) => what switch
    {
        "api_id" => _options.ApiId.ToString(),
        "api_hash" => _options.ApiHash,
        "phone_number" => _options.PhoneNumber,
        "session_pathname" => Path.GetFullPath(_options.SessionPath),
        "verification_code" => Prompt("Telegram verification code: "),
        "password" => Prompt("Telegram 2FA password: "),
        _ => null
    };

    private static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine() ?? string.Empty;
    }
}
