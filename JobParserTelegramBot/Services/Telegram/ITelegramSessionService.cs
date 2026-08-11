using TL;
using WTelegram;

namespace JobParserTelegramBot.Services.Telegram;

public interface ITelegramSessionService
{
    Client Client { get; }
    UpdateManager? Manager { get; }
    User? Self { get; }
    void SetUpdateHandler(Func<Update, Task> handler);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<InputPeer?> ResolvePeerAsync(long chatId, string? username, CancellationToken cancellationToken = default);
}
