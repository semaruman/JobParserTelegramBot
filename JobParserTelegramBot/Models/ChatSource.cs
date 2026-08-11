using System.Text.Json.Serialization;

namespace JobParserTelegramBot.Models;

public sealed class ChatSource
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
