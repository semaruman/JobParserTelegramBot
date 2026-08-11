using JobParserTelegramBot.Models;

namespace JobParserTelegramBot.Services.Telegram;

public interface IChatManagementService
{
    Task<IReadOnlyList<ChatSource>> ListAsync(CancellationToken cancellationToken = default);
    Task<ChatSource> AddAsync(string usernameOrId, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string usernameOrId, CancellationToken cancellationToken = default);
    Task<bool> RemoveByIdAsync(long id, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken = default);
}
