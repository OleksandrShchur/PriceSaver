namespace PriceSaver.Server.Handlers
{
    public interface ISubscriptionHandler
    {
        Task SendSubscriptionsAsync(long chatId, CancellationToken cancellationToken);
        Task DeleteSubscriptionAsync(long chatId, string text, CancellationToken cancellationToken);
        Task CreateSubscriptionAsync(long chatId, string? username, string url, CancellationToken cancellationToken);
    }
}
