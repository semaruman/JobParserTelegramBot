namespace JobParserTelegramBot.Configuration;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public int ApiId { get; set; }
    public string ApiHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string SessionPath { get; set; } = "data/wtelegram.session";
}
