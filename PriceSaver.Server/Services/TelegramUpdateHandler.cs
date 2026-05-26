using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PriceSaver.Server.Services
{
    public class TelegramUpdateHandler : ITelegramUpdateHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly ITelegramService _telegram;
        private readonly IPriceParser[] _parsers;
        private readonly TelegramOptions _options;
        private readonly ILogger<TelegramUpdateHandler> _logger;

        public TelegramUpdateHandler(
            ApplicationDbContext db,
            ITelegramService telegram,
            IEnumerable<IPriceParser> parsers,
            IOptions<TelegramOptions> options,
            ILogger<TelegramUpdateHandler> logger)
        {
            _db = db;
            _telegram = telegram;
            _parsers = parsers.ToArray();
            _options = options.Value;
            _logger = logger;
        }

        public async Task HandleAsync(Update update, CancellationToken cancellationToken = default)
        {
            if (update.Message is not { Type: MessageType.Text } message)
            {
                return;
            }

            var chatId = message.Chat.Id;
            var text = message.Text?.Trim() ?? string.Empty;

            if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureUserExistsAsync(chatId, message.From?.Username, cancellationToken);
                await _telegram.SendMessageAsync(
                    chatId,
                    $"Welcome to {_options.BotDisplayName}. Send a supported store product link to create a price subscription. Use /my_subscriptions to view them.",
                    cancellationToken);
                return;
            }

            if (text.StartsWith("/my_subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                await SendSubscriptionsAsync(chatId, cancellationToken);
                return;
            }

            if (text.StartsWith("/delete_subscription", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("/delete", StringComparison.OrdinalIgnoreCase))
            {
                await DeleteSubscriptionAsync(chatId, text, cancellationToken);
                return;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                await CreateSubscriptionAsync(chatId, message.From?.Username, uri.ToString(), cancellationToken);
                return;
            }

            await _telegram.SendMessageAsync(
                chatId,
                "Send a direct product link from ATB, Silpo, Metro, or Epicentr. Use /my_subscriptions to manage saved products.",
                cancellationToken);
        }

        private async Task EnsureUserExistsAsync(long telegramId, string? username, CancellationToken cancellationToken)
        {
            var user = await _db.Users.FindAsync([telegramId], cancellationToken);
            if (user is null)
            {
                _db.Users.Add(new Models.User
                {
                    TelegramId = telegramId,
                    Username = username
                });
            }
            else if (!string.Equals(user.Username, username, StringComparison.Ordinal) && username is not null)
            {
                user.Username = username;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task SendSubscriptionsAsync(long chatId, CancellationToken cancellationToken)
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

            var lines = subscriptions.Select(subscription =>
                $"{subscription.Id}\n{subscription.ProductName}\n{subscription.StoreType} - {subscription.CurrentPrice:0.##} UAH\n{subscription.ProductUrl}\nDelete: /delete_subscription {subscription.Id}");

            await _telegram.SendMessageAsync(chatId, string.Join("\n\n", lines), cancellationToken);
        }

        private async Task DeleteSubscriptionAsync(long chatId, string text, CancellationToken cancellationToken)
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

        private async Task CreateSubscriptionAsync(long chatId, string? username, string url, CancellationToken cancellationToken)
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
                await EnsureUserExistsAsync(chatId, username, cancellationToken);

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

                await _telegram.SendMessageAsync(
                    chatId,
                    $"Subscription created:\n{name}\n{storeType} - {price:0.##} UAH\n{url}",
                    cancellationToken);
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
