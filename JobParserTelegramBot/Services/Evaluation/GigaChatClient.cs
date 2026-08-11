using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobParserTelegramBot.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobParserTelegramBot.Services.Evaluation;

public sealed class GigaChatClient : IGigaChatClient
{
    private readonly HttpClient _httpClient;
    private readonly GigaChatOptions _options;
    private readonly ILogger<GigaChatClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public GigaChatClient(HttpClient httpClient, IOptions<GigaChatOptions> options, ILogger<GigaChatClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new ChatCompletionRequest
        {
            Model = _options.Model,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userMessage }
            ],
            Temperature = 0.3
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat chat/completions failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"GigaChat API error: {(int)response.StatusCode}");
        }

        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body);
        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("GigaChat returned empty content.");
        }

        return content;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.AuthorizationKey))
            {
                throw new InvalidOperationException("GigaChat:AuthorizationKey is not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.OAuthUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _options.AuthorizationKey);
            request.Headers.Add("RqUID", Guid.NewGuid().ToString());
            request.Content = new StringContent(
                $"scope={Uri.EscapeDataString(_options.Scope)}",
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GigaChat OAuth failed: {Status} {Body}", response.StatusCode, body);
                throw new InvalidOperationException($"GigaChat OAuth error: {(int)response.StatusCode}");
            }

            var tokenResponse = JsonSerializer.Deserialize<OAuthResponse>(body)
                ?? throw new InvalidOperationException("Failed to parse GigaChat OAuth response.");

            _accessToken = tokenResponse.AccessToken
                ?? throw new InvalidOperationException("GigaChat OAuth response has no access_token.");

            if (tokenResponse.ExpiresAt > 0)
            {
                _tokenExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(tokenResponse.ExpiresAt).AddMinutes(-1);
            }
            else
            {
                _tokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(25);
            }

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed class OAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_at")]
        public long ExpiresAt { get; set; }
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public Choice[]? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
