using PriceSaver.Server.Models;

namespace PriceSaver.Server.Services
{
    public interface IUserService
    {
        Task EnsureUserExistsAsync(long telegramId, string? username, CancellationToken cancellationToken);

        Task<User?> GetAsync(long telegramId, CancellationToken cancellationToken);

        Task<bool> HasLocationAsync(long telegramId, CancellationToken cancellationToken);

        Task SetConversationStateAsync(
            long telegramId,
            string state,
            string? payload,
            CancellationToken cancellationToken);

        Task SaveLocationAsync(
            long telegramId,
            decimal latitude,
            decimal longitude,
            string locationName,
            CancellationToken cancellationToken);
    }
}
