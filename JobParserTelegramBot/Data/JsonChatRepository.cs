using System.Text.Json;
using JobParserTelegramBot.Configuration;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Options;

namespace JobParserTelegramBot.Data;

public sealed class JsonChatRepository : IChatRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonChatRepository(IOptions<EvaluationOptions> options)
    {
        _path = Path.GetFullPath(options.Value.ChatsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path))
        {
            File.WriteAllText(_path, "[]");
        }
    }

    public async Task<IReadOnlyList<ChatSource>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await ReadInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ChatSource?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var chats = await GetAllAsync(cancellationToken);
        return chats.FirstOrDefault(c => c.Id == id);
    }

    public async Task<ChatSource?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUsername(username);
        var chats = await GetAllAsync(cancellationToken);
        return chats.FirstOrDefault(c =>
            !string.IsNullOrEmpty(c.Username) &&
            string.Equals(NormalizeUsername(c.Username), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddOrUpdateAsync(ChatSource chat, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var chats = (await ReadInternalAsync(cancellationToken)).ToList();
            var existing = chats.FirstOrDefault(c => c.Id == chat.Id);
            if (existing is null && !string.IsNullOrEmpty(chat.Username))
            {
                var username = NormalizeUsername(chat.Username);
                existing = chats.FirstOrDefault(c =>
                    !string.IsNullOrEmpty(c.Username) &&
                    string.Equals(NormalizeUsername(c.Username), username, StringComparison.OrdinalIgnoreCase));
            }

            if (existing is not null)
            {
                existing.Id = chat.Id;
                existing.Title = chat.Title;
                existing.Username = chat.Username;
                existing.Enabled = chat.Enabled;
            }
            else
            {
                chats.Add(chat);
            }

            await WriteInternalAsync(chats, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var chats = (await ReadInternalAsync(cancellationToken)).ToList();
            var removed = chats.RemoveAll(c => c.Id == id) > 0;
            if (removed)
            {
                await WriteInternalAsync(chats, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUsername(username);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var chats = (await ReadInternalAsync(cancellationToken)).ToList();
            var removed = chats.RemoveAll(c =>
                !string.IsNullOrEmpty(c.Username) &&
                string.Equals(NormalizeUsername(c.Username), normalized, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                await WriteInternalAsync(chats, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ChatSource>> ReadInternalAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_path);
        var chats = await JsonSerializer.DeserializeAsync<List<ChatSource>>(stream, JsonOptions, cancellationToken);
        return chats ?? [];
    }

    private async Task WriteInternalAsync(IReadOnlyList<ChatSource> chats, CancellationToken cancellationToken)
    {
        var tempPath = _path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, chats, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _path, overwrite: true);
    }

    private static string NormalizeUsername(string username) =>
        username.Trim().TrimStart('@');
}
