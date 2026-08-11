using System.Text.Json;
using JobParserTelegramBot.Configuration;
using JobParserTelegramBot.Models;
using Microsoft.Extensions.Options;

namespace JobParserTelegramBot.Data;

public sealed class JsonProcessedMessageStore : IProcessedMessageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonProcessedMessageStore(IOptions<EvaluationOptions> options)
    {
        _path = Path.GetFullPath(options.Value.ProcessedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path))
        {
            File.WriteAllText(_path, "[]");
        }
    }

    public async Task<bool> TryMarkProcessedAsync(long chatId, int messageId, CancellationToken cancellationToken = default)
    {
        var key = $"{chatId}:{messageId}";
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadInternalAsync(cancellationToken);
            if (records.Any(r => r.Key == key))
            {
                return false;
            }

            records.Add(new ProcessedMessageRecord
            {
                Key = key,
                ProcessedAtUtc = DateTimeOffset.UtcNow
            });
            await WriteInternalAsync(records, cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task CleanupAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var threshold = DateTimeOffset.UtcNow - maxAge;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadInternalAsync(cancellationToken);
            var filtered = records.Where(r => r.ProcessedAtUtc >= threshold).ToList();
            if (filtered.Count != records.Count)
            {
                await WriteInternalAsync(filtered, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ProcessedMessageRecord>> ReadInternalAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_path);
        var records = await JsonSerializer.DeserializeAsync<List<ProcessedMessageRecord>>(stream, JsonOptions, cancellationToken);
        return records ?? [];
    }

    private async Task WriteInternalAsync(IReadOnlyList<ProcessedMessageRecord> records, CancellationToken cancellationToken)
    {
        var tempPath = _path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _path, overwrite: true);
    }
}
