namespace JobParserTelegramBot.Configuration;

public sealed class GigaChatOptions
{
    public const string SectionName = "GigaChat";

    public string AuthorizationKey { get; set; } = string.Empty;
    public string Scope { get; set; } = "GIGACHAT_API_PERS";
    public string Model { get; set; } = "GigaChat";
    public string BaseUrl { get; set; } = "https://gigachat.devices.sberbank.ru/api/v1";
    public string OAuthUrl { get; set; } = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
}
