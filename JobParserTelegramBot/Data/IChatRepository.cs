using JobParserTelegramBot.Models;

namespace JobParserTelegramBot.Data;

public interface IChatRepository
{
    Task<IReadOnlyList<ChatSource>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ChatSource?> FindByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ChatSource?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(ChatSource chat, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default);
    Task<bool> RemoveByUsernameAsync(string username, CancellationToken cancellationToken = default);
}
