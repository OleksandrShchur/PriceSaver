using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;

namespace PriceSaver.Server.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IUserService _userService;
        private readonly TelegramOptions _options;
        private readonly ILogger<SubscriptionService> _logger;
        private readonly IPriceParser[] _parsers;

        public SubscriptionService(
            ApplicationDbContext db,
            IUserService userService,
            IOptions<TelegramOptions> options,
            ILogger<SubscriptionService> logger,
            IEnumerable<IPriceParser> parsers)
        {
            _db = db;
            _userService = userService;
            _options = options.Value;
            _logger = logger;
            _parsers = parsers.ToArray();
        }

        public Task<List<Subscription>> GetActiveSubscriptionsAsync(long userId, CancellationToken cancellationToken)
        {
            return _db.Subscriptions
                .Where(s => s.UserId == userId && s.IsActive)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<DeactivateSubscriptionResult> DeactivateSubscriptionAsync(long userId, Guid subscriptionId, CancellationToken cancellationToken)
        {
            var subscription = await _db.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId && s.IsActive, cancellationToken);

            if (subscription is null)
                return new DeactivateSubscriptionResult(DeactivateSubscriptionStatus.NotFound);

            subscription.IsActive = false;
            await _db.SaveChangesAsync(cancellationToken);

            return new DeactivateSubscriptionResult(DeactivateSubscriptionStatus.Success);
        }

        public async Task<CreateSubscriptionResult> CreateSubscriptionAsync(long userId, string? username, string url, CancellationToken cancellationToken)
        {
            var existingActive = await _db.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.ProductUrl == url && s.IsActive, cancellationToken);

            if (existingActive is not null)
                return new CreateSubscriptionResult(CreateSubscriptionStatus.AlreadyActive, existingActive);

            var parser = _parsers.FirstOrDefault(p => p.CanParse(url));
            if (parser is null)
                return new CreateSubscriptionResult(CreateSubscriptionStatus.UnsupportedStore);

            var activeCount = await _db.Subscriptions
                .CountAsync(s => s.UserId == userId && s.IsActive, cancellationToken);

            if (activeCount >= _options.MaxSubscriptionsPerUser)
                return new CreateSubscriptionResult(CreateSubscriptionStatus.LimitReached);

            try
            {
                await _userService.EnsureUserExistsAsync(userId, username, cancellationToken);

                var (name, price) = await parser.ParseAsync(url, cancellationToken);
                var storeType = InferStoreType(parser.StoreKey);

                var inactive = await _db.Subscriptions
                    .FirstOrDefaultAsync(s => s.UserId == userId && s.ProductUrl == url && !s.IsActive, cancellationToken);

                Subscription subscription;

                if (inactive is not null)
                {
                    inactive.IsActive = true;
                    inactive.ProductName = name;
                    inactive.CurrentPrice = price;
                    inactive.LastCheckedDate = DateTime.UtcNow;
                    subscription = inactive;
                }
                else
                {
                    subscription = new Subscription
                    {
                        UserId = userId,
                        ProductUrl = url,
                        StoreType = storeType,
                        ProductName = name,
                        CurrentPrice = price,
                        LastCheckedDate = DateTime.UtcNow
                    };
                    _db.Subscriptions.Add(subscription);
                }

                await _db.SaveChangesAsync(cancellationToken);

                var status = inactive is not null
                    ? CreateSubscriptionStatus.Reactivated
                    : CreateSubscriptionStatus.Created;

                return new CreateSubscriptionResult(status, subscription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create subscription for user {UserId} and url {Url}", userId, url);

                return new CreateSubscriptionResult(CreateSubscriptionStatus.ParseFailed);
            }
        }

        private static StoreType InferStoreType(string key) => key.ToLowerInvariant() switch
        {
            "atb" => StoreType.ATB,
            "silpo" => StoreType.Silpo,
            _ => StoreType.Unknown
        };
    }
}
