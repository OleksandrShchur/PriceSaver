using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Data;
using PriceSaver.Server.Extensions;
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
                await _telegram.SendMessageAsync(
                    chatId,
                    "⚠️ <b>У Вас немає активних підписок.</b>",
                    cancellationToken);
                return;
            }

            foreach (var subscription in subscriptions)
            {
                var safeProductName = WebUtility.HtmlEncode(subscription.ProductName);
                var safeStoreDescription = WebUtility.HtmlEncode(subscription.StoreType.GetDescription());

                var message = $"📦 <b>{safeProductName}</b>\n\n" +
                              $"🏪 <b>Магазин:</b> {safeStoreDescription}\n" +
                              $"💰 <b>Ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH\n\n" +
                              $"🔗 <a href=\"{subscription.ProductUrl}\">Перейти до товару</a>";

                await _telegram.SendMessageWithInlineButtonAsync(
                    chatId,
                    message,
                    "🗑️ Видалити",
                    $"sub_remove_{subscription.Id}",
                    cancellationToken);
            }
        }

        public async Task HandleRemoveSubscriptionCallbackAsync(long chatId, string callbackQueryId, string subscriptionId, int messageId, CancellationToken cancellationToken)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Некоректний Id підписки.", true, cancellationToken);
                    return;
                }

                var subscription = await _db.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == subscriptionGuid && s.UserId == chatId && s.IsActive, cancellationToken);

                if (subscription is null)
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Підписку видалено раніше.", true, cancellationToken);
                    return;
                }

                subscription.IsActive = false;
                await _db.SaveChangesAsync(cancellationToken);

                await _telegram.DeleteMessageAsync(chatId, messageId, cancellationToken);
                await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "✅ Підписку видалено.", false, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle remove subscription callback for chat {ChatId} and subscription {SubscriptionId}", chatId, subscriptionId);
                await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Помилка при видаленні підписки.", true, cancellationToken);
            }
        }

        public async Task CreateSubscriptionAsync(long chatId, string? username, string url, CancellationToken cancellationToken)
        {
            var existingSubscription = await _db.Subscriptions
                .FirstOrDefaultAsync(
                    s => s.UserId == chatId && s.ProductUrl == url && s.IsActive,
                    cancellationToken);

            if (existingSubscription is not null)
            {
                var safeExistingName = WebUtility.HtmlEncode(existingSubscription.ProductName);
                await _telegram.SendMessageAsync(
                    chatId,
                    $"ℹ️ <b>Ця підписка вже існує у Вашому списку.</b>\n\n" +
                    $"📦 <b>{safeExistingName}</b>\n" +
                    $"💰 <b>Поточна ціна:</b> <code>{existingSubscription.CurrentPrice:0.##}</code> UAH\n\n" +
                    $"🔗 <a href=\"{existingSubscription.ProductUrl}\">Перейти до товару</a>",
                    cancellationToken);

                return;
            }

            var parser = _parsers.FirstOrDefault(candidate => candidate.CanParse(url));
            if (parser is null)
            {
                await _telegram.SendMessageAsync(
                    chatId,
                    "❌ <b>Вказаний магазин ще не підтримується нами.</b>",
                    cancellationToken);

                return;
            }

            var activeSubscriptionCount = await _db.Subscriptions
                .CountAsync(subscription => subscription.UserId == chatId && subscription.IsActive, cancellationToken);

            if (activeSubscriptionCount >= _options.MaxSubscriptionsPerUser)
            {
                await _telegram.SendMessageAsync(
                    chatId,
                    $"🚫 <b>Досягнуто ліміту підписок!</b>\nМаксимально дозволено: <code>{_options.MaxSubscriptionsPerUser}</code>.",
                    cancellationToken);
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

                var safeName = WebUtility.HtmlEncode(name);
                var confirmationMessage = $"✅ <b>Підписку створено!</b>\n\n" +
                                          $"📦 <b>{safeName}</b>\n" +
                                          $"🏪 <b>Магазин:</b> {storeType}\n" +
                                          $"💰 <b>Ціна:</b> <code>{price:0.##}</code> UAH\n\n" +
                                          $"🔗 <a href=\"{url}\">Перейти до товару</a>";

                await _telegram.SendMessageAsync(chatId, confirmationMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Telegram subscription for chat {ChatId} and url {Url}", chatId, url);
                await _telegram.SendMessageAsync(
                    chatId,
                    "❌ <b>Неможливо отримати інформацію про продукт за посиланням.</b>\nБудь ласка, перевірте правильність посилання.",
                    cancellationToken);
            }
        }

        private static StoreType InferStoreType(string key) => key.ToLowerInvariant() switch
        {
            "atb" => StoreType.ATB,
            _ => StoreType.Unknown
        };
    }
}
