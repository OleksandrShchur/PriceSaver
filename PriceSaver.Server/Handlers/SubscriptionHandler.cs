using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Handlers
{
    public class SubscriptionHandler : ISubscriptionHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly ITelegramService _telegram;
        private readonly ILogger<TelegramUpdateHandler> _logger;
        private readonly TelegramOptions _options;
        private readonly IUserService _userService;
        private readonly IPriceParser[] _parsers;

        public SubscriptionHandler(
            ApplicationDbContext db,
            ITelegramService telegram,
            IOptions<TelegramOptions> options,
            ILogger<TelegramUpdateHandler> logger,
            IUserService userService,
            IEnumerable<IPriceParser> parsers)
        {
            _db = db;
            _telegram = telegram;
            _options = options.Value;
            _logger = logger;
            _userService = userService;
            _parsers = parsers.ToArray();
        }

        public async Task SendSubscriptionsAsync(long chatId, CancellationToken cancellationToken)
        {
            var subscriptions = await _db.Subscriptions
                .Where(subscription => subscription.UserId == chatId && subscription.IsActive)
                .OrderBy(subscription => subscription.CreatedAt)
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
            {
                await _telegram.SendMessageAsync(chatId, "You have no active subscriptions.", cancellationToken);
                return;
            }

            foreach (var subscription in subscriptions)
            {
                var message = $"📦 {subscription.ProductName}\n\n" +
                             $"🏪 {subscription.StoreType}\n" +
                             $"💰 {subscription.CurrentPrice:0.##} UAH\n\n" +
                             $"🔗 {subscription.ProductUrl}";

                await _telegram.SendMessageWithInlineButtonAsync(
                    chatId,
                    message,
                    "🗑️ Remove",
                    $"sub_remove_{subscription.Id}",
                    cancellationToken);
            }
        }

        public async Task DeleteSubscriptionAsync(long chatId, string text, CancellationToken cancellationToken)
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !Guid.TryParse(parts[1], out var subscriptionId))
            {
                await _telegram.SendMessageAsync(chatId, "Use /delete_subscription <subscription_id>.", cancellationToken);
                return;
            }

            var subscription = await _db.Subscriptions
                .FirstOrDefaultAsync(item => item.Id == subscriptionId && item.UserId == chatId && item.IsActive, cancellationToken);

            if (subscription is null)
            {
                await _telegram.SendMessageAsync(chatId, "Subscription was not found.", cancellationToken);
                return;
            }

            subscription.IsActive = false;
            await _db.SaveChangesAsync(cancellationToken);

            await _telegram.SendMessageAsync(chatId, "Subscription deleted.", cancellationToken);
        }

        public async Task HandleRemoveSubscriptionCallbackAsync(long chatId, string callbackQueryId, string subscriptionId, int messageId, CancellationToken cancellationToken)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Invalid subscription ID.", true, cancellationToken);
                    return;
                }

                var subscription = await _db.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == subscriptionGuid && s.UserId == chatId && s.IsActive, cancellationToken);

                if (subscription is null)
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Subscription already removed.", true, cancellationToken);
                    return;
                }

                subscription.IsActive = false;
                await _db.SaveChangesAsync(cancellationToken);

                await _telegram.DeleteMessageAsync(chatId, messageId, cancellationToken);
                await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "✅ Subscription removed.", false, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle remove subscription callback for chat {ChatId} and subscription {SubscriptionId}", chatId, subscriptionId);
                await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Error removing subscription.", true, cancellationToken);
            }
        }

        public async Task CreateSubscriptionAsync(long chatId, string? username, string url, CancellationToken cancellationToken)
        {
            var parser = _parsers.FirstOrDefault(candidate => candidate.CanParse(url));
            if (parser is null)
            {
                await _telegram.SendMessageAsync(chatId, "That store is not supported yet.", cancellationToken);
                return;
            }

            var activeSubscriptionCount = await _db.Subscriptions
                .CountAsync(subscription => subscription.UserId == chatId && subscription.IsActive, cancellationToken);

            if (activeSubscriptionCount >= _options.MaxSubscriptionsPerUser)
            {
                await _telegram.SendMessageAsync(chatId, $"Subscription limit reached ({_options.MaxSubscriptionsPerUser}).", cancellationToken);
                return;
            }

            try
            {
                await _userService.EnsureUserExistsAsync(chatId, username, cancellationToken);

                var (name, price) = await parser.ParseAsync(url, cancellationToken);
                var storeType = InferStoreType(parser.StoreKey);

                var subscription = new Subscription
                {
                    UserId = chatId,
                    ProductUrl = url,
                    StoreType = storeType,
                    ProductName = name,
                    CurrentPrice = price,
                    LastCheckedDate = DateTime.UtcNow
                };

                _db.Subscriptions.Add(subscription);
                await _db.SaveChangesAsync(cancellationToken);

                var confirmationMessage = $"✅ Subscription created!\n\n" +
                                        $"📦 {name}\n" +
                                        $"🏪 {storeType} - {price:0.##} UAH\n" +
                                        $"🔗 {url}";

                await _telegram.SendMessageAsync(chatId, confirmationMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Telegram subscription for chat {ChatId} and url {Url}", chatId, url);
                await _telegram.SendMessageAsync(chatId, "Could not read product details from that link.", cancellationToken);
            }
        }

        private static StoreType InferStoreType(string key) => key.ToLowerInvariant() switch
        {
            "atb" => StoreType.ATB,
            "silpo" => StoreType.Silpo,
            "metro" => StoreType.Metro,
            "epicentr" => StoreType.Epicentr,
            _ => StoreType.Unknown
        };
    }
}
