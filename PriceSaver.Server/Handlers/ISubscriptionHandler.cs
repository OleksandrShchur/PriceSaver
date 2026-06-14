namespace PriceSaver.Server.Handlers
{
    public interface ISubscriptionHandler
    {
        Task SendSubscriptionsAsync(long chatId, CancellationToken cancellationToken);
        Task CreateSubscriptionAsync(long chatId, string? username, string url, CancellationToken cancellationToken);
        Task HandleRemoveSubscriptionCallbackAsync(long chatId, string callbackQueryId, string subscriptionId, int messageId, CancellationToken cancellationToken);
        Task HandleToggleNotifyOnIncreaseCallbackAsync(long chatId, string callbackQueryId, string subscriptionId, int messageId, CancellationToken cancellationToken);
    }
}
