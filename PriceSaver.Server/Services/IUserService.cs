namespace PriceSaver.Server.Services
{
    public interface IUserService
    {
        Task EnsureUserExistsAsync(long telegramId, string? username, CancellationToken cancellationToken);
    }
}
