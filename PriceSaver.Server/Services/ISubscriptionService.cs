using PriceSaver.Server.Models;

namespace PriceSaver.Server.Services
{
    public interface ISubscriptionService
    {
        Task<List<Subscription>> GetActiveSubscriptionsAsync(long userId, CancellationToken cancellationToken);

        Task<CreateSubscriptionResult> CreateSubscriptionAsync(long userId, string? username, string url, CancellationToken cancellationToken);

        Task<DeactivateSubscriptionResult> DeactivateSubscriptionAsync(long userId, Guid subscriptionId, CancellationToken cancellationToken);
    }
}
