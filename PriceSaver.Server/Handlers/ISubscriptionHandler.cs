namespace PriceSaver.Server.Handlers
{
    public interface ISubscriptionHandler
    {
        Task SendSubscriptionsAsync(long chatId, CancellationToken cancellationToken);
        Task CreateSubscriptionAsync(long chatId, string? username, string url, CancellationToken cancellationToken);
        Task HandleSelectSubscriptionCallbackAsync(long chatId, string callbackQueryId, int page, string subscriptionId, int messageId, CancellationToken cancellationToken);
        Task HandleListPageCallbackAsync(long chatId, string callbackQueryId, int page, int messageId, CancellationToken cancellationToken);
        Task HandleRemoveSubscriptionCallbackAsync(long chatId, string callbackQueryId, int page, string subscriptionId, int messageId, CancellationToken cancellationToken);
        Task HandleToggleNotifyOnIncreaseCallbackAsync(long chatId, string callbackQueryId, int page, string subscriptionId, int messageId, CancellationToken cancellationToken);
    }
}
