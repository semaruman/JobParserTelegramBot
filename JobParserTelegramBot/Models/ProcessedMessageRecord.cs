using System.Text.Json.Serialization;

namespace JobParserTelegramBot.Models;

public sealed class ProcessedMessageRecord
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("processedAtUtc")]
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
