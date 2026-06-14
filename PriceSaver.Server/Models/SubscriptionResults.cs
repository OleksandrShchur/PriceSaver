namespace PriceSaver.Server.Models
{
    public enum CreateSubscriptionStatus
    {
        Created,
        Reactivated,
        AlreadyActive,
        UnsupportedStore,
        LimitReached,
        ParseFailed
    }

    public record CreateSubscriptionResult(
        CreateSubscriptionStatus Status,
        Subscription? Subscription = null);

    public enum DeactivateSubscriptionStatus
    {
        Success,
        NotFound
    }

    public record DeactivateSubscriptionResult(DeactivateSubscriptionStatus Status);

    public enum ToggleNotifyOnIncreaseStatus
    {
        Success,
        NotFound
    }

    public record ToggleNotifyOnIncreaseResult(
        ToggleNotifyOnIncreaseStatus Status,
        Subscription? Subscription = null);
}
