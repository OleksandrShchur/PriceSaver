using System.Net;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Handlers
{
    public class SubscriptionHandler : ISubscriptionHandler
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ITelegramService _telegram;
        private readonly ILogger<SubscriptionHandler> _logger;
        private readonly TelegramOptions _options;

        public SubscriptionHandler(
            ISubscriptionService subscriptionService,
            ITelegramService telegram,
            IOptions<TelegramOptions> options,
            ILogger<SubscriptionHandler> logger)
        {
            _subscriptionService = subscriptionService;
            _telegram = telegram;
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendSubscriptionsAsync(long chatId, CancellationToken cancellationToken)
        {
            _logger.LogDebug(
                "Received /{Command} from UserId: {UserId} (@{Username})",
                "my_subscriptions",
                chatId,
                (string?)null);

            try
            {
                var subscriptions = await _subscriptionService.GetActiveSubscriptionsAsync(chatId, cancellationToken);

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
                    await _telegram.SendMessageWithKeyboardAsync(
                        chatId,
                        BuildSubscriptionMessage(subscription),
                        BuildSubscriptionKeyboard(subscription),
                        cancellationToken);
                }

                _logger.LogInformation(
                    "Sent subscription list to UserId: {UserId}. Count: {Count}",
                    chatId,
                    subscriptions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in /{Command} handler for UserId: {UserId}", "my_subscriptions", chatId);
                await _telegram.SendMessageAsync(
                    chatId,
                    "❌ Сталася непередбачена помилка. Спробуйте пізніше або зверніться до підтримки.",
                    cancellationToken);
            }
        }

        public async Task HandleRemoveSubscriptionCallbackAsync(long chatId, string callbackQueryId, string subscriptionId, int messageId, CancellationToken cancellationToken)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
                {
                    _logger.LogWarning(
                        "Failed to parse callback data '{CallbackData}' for UserId: {UserId}",
                        subscriptionId,
                        chatId);

                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Некоректний Id підписки.", true, cancellationToken);
                    return;
                }

                var result = await _subscriptionService.DeactivateSubscriptionAsync(chatId, subscriptionGuid, cancellationToken);

                if (result.Status == DeactivateSubscriptionStatus.NotFound)
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Підписку видалено раніше.", true, cancellationToken);
                    return;
                }

                await _telegram.AnswerCallbackQueryAsync(
                    callbackQueryId,
                    "Підписку видалено. Ми більше не відстежуватимемо цей товар.",
                    false,
                    cancellationToken);

                await _telegram.DeleteMessageAsync(chatId, messageId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in /{Command} handler for UserId: {UserId}", "sub_remove", chatId);
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQueryId,
                    "❌ Сталася непередбачена помилка. Спробуйте пізніше або зверніться до підтримки.",
                    true,
                    cancellationToken);
            }
        }

        public async Task HandleToggleNotifyOnIncreaseCallbackAsync(long chatId, string callbackQueryId, string subscriptionId, int messageId, CancellationToken cancellationToken)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
                {
                    _logger.LogWarning(
                        "Failed to parse callback data '{CallbackData}' for UserId: {UserId}",
                        subscriptionId,
                        chatId);

                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Некоректний Id підписки.", true, cancellationToken);
                    return;
                }

                var result = await _subscriptionService.ToggleNotifyOnIncreaseAsync(chatId, subscriptionGuid, cancellationToken);

                if (result.Status == ToggleNotifyOnIncreaseStatus.NotFound || result.Subscription is null)
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Підписку не знайдено.", true, cancellationToken);
                    return;
                }

                if (messageId > 0)
                {
                    await _telegram.EditMessageTextAsync(
                        chatId,
                        messageId,
                        BuildSubscriptionMessage(result.Subscription),
                        BuildSubscriptionKeyboard(result.Subscription),
                        cancellationToken);
                }

                var answer = result.Subscription.NotifyOnIncrease
                    ? "Сповіщення про здорожчання увімкнено."
                    : "Сповіщення про здорожчання вимкнено.";

                await _telegram.AnswerCallbackQueryAsync(callbackQueryId, answer, false, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in /{Command} handler for UserId: {UserId}", "sub_toggle_increase", chatId);
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQueryId,
                    "❌ Сталася непередбачена помилка. Спробуйте пізніше або зверніться до підтримки.",
                    true,
                    cancellationToken);
            }
        }

        public async Task CreateSubscriptionAsync(long chatId, string? username, string url, CancellationToken cancellationToken)
        {
            _logger.LogDebug(
                "Received /{Command} from UserId: {UserId} (@{Username})",
                "subscribe",
                chatId,
                username);

            try
            {
                var result = await _subscriptionService.CreateSubscriptionAsync(chatId, username, url, cancellationToken);

                var message = result.Status switch
                {
                    CreateSubscriptionStatus.AlreadyActive => BuildAlreadyActiveMessage(result.Subscription!),
                    CreateSubscriptionStatus.UnsupportedStore => "❌ <b>Вказаний магазин ще не підтримується нами.</b>",
                    CreateSubscriptionStatus.LimitReached => $"🚫 <b>Досягнуто ліміту підписок!</b>\nМаксимально дозволено: <code>{_options.MaxSubscriptionsPerUser}</code>.",
                    CreateSubscriptionStatus.ParseFailed => "⚠️ Не вдалося отримати ціну для вказаного товару. Перевірте посилання та спробуйте ще раз.",
                    CreateSubscriptionStatus.Created or
                    CreateSubscriptionStatus.Reactivated => BuildConfirmationMessage(result.Subscription!),
                    _ => "❌ <b>Сталася невідома помилка.</b>"
                };

                await _telegram.SendMessageAsync(chatId, message, cancellationToken);
            }
            catch (PriceParseException ex)
            {
                _logger.LogWarning(ex, "Price parse validation error for UserId: {UserId}, Url: {Url}", chatId, url);
                await _telegram.SendMessageAsync(
                    chatId,
                    "⚠️ Не вдалося отримати ціну для вказаного товару. Перевірте посилання та спробуйте ще раз.",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in /{Command} handler for UserId: {UserId}", "subscribe", chatId);
                await _telegram.SendMessageAsync(
                    chatId,
                    "❌ Сталася непередбачена помилка. Спробуйте пізніше або зверніться до підтримки.",
                    cancellationToken);
            }
        }

        private static string BuildAlreadyActiveMessage(Subscription subscription)
        {
            var safeName = WebUtility.HtmlEncode(subscription.ProductName);
            var safeStoreDescription = WebUtility.HtmlEncode(subscription.StoreType.GetDescription());
            var safeProductUrl = WebUtility.HtmlEncode(subscription.ProductUrl);
            return $"ℹ️ <b>Ця підписка вже існує у Вашому списку.</b>\n\n" +
                   $"📦 <a href=\"{safeProductUrl}\"><b>{safeName}</b></a>\n" +
                   $"🏪 <b>Магазин:</b> {safeStoreDescription}\n" +
                   $"💰 <b>Поточна ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH";
        }

        private static string BuildSubscriptionMessage(Subscription subscription)
        {
            var safeProductName = WebUtility.HtmlEncode(subscription.ProductName);
            var safeStoreDescription = WebUtility.HtmlEncode(subscription.StoreType.GetDescription());
            var safeProductUrl = WebUtility.HtmlEncode(subscription.ProductUrl);

            return $"📦 <a href=\"{safeProductUrl}\"><b>{safeProductName}</b></a>\n\n" +
                   $"🏪 <b>Магазин:</b> {safeStoreDescription}\n" +
                   $"💰 <b>Ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH";
        }

        private static InlineKeyboardMarkup BuildSubscriptionKeyboard(Subscription subscription)
        {
            var notifyButtonText = subscription.NotifyOnIncrease
                ? "🔕 Не сповіщати про здорожчання"
                : "🔔 Сповіщати про здорожчання";

            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(notifyButtonText, $"sub_toggle_increase_{subscription.Id}") },
                new[] { InlineKeyboardButton.WithCallbackData("🗑️ Видалити", $"sub_remove_{subscription.Id}") }
            });
        }

        private static string BuildConfirmationMessage(Subscription subscription)
        {
            var safeName = WebUtility.HtmlEncode(subscription.ProductName);
            var safeStoreDescription = WebUtility.HtmlEncode(subscription.StoreType.GetDescription());
            var safeProductUrl = WebUtility.HtmlEncode(subscription.ProductUrl);
            return $"✅ <b>Підписку створено!</b>\n\n" +
                   $"📦 <a href=\"{safeProductUrl}\"><b>{safeName}</b></a>\n" +
                   $"🏪 <b>Магазин:</b> {safeStoreDescription}\n" +
                   $"💰 <b>Ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH";
        }
    }
}
