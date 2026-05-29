using System.Net;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using Microsoft.Extensions.Options;

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

                var result = await _subscriptionService.DeactivateSubscriptionAsync(chatId, subscriptionGuid, cancellationToken);

                if (result.Status == DeactivateSubscriptionStatus.NotFound)
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Підписку видалено раніше.", true, cancellationToken);
                    return;
                }

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
            var result = await _subscriptionService.CreateSubscriptionAsync(chatId, username, url, cancellationToken);

            var message = result.Status switch
            {
                CreateSubscriptionStatus.AlreadyActive => BuildAlreadyActiveMessage(result.Subscription!),
                CreateSubscriptionStatus.UnsupportedStore => "❌ <b>Вказаний магазин ще не підтримується нами.</b>",
                CreateSubscriptionStatus.LimitReached => $"🚫 <b>Досягнуто ліміту підписок!</b>\nМаксимально дозволено: <code>{_options.MaxSubscriptionsPerUser}</code>.",
                CreateSubscriptionStatus.ParseFailed => "❌ <b>Неможливо отримати інформацію про продукт за посиланням.</b>\nБудь ласка, перевірте правильність посилання.",
                CreateSubscriptionStatus.Created or
                CreateSubscriptionStatus.Reactivated => BuildConfirmationMessage(result.Subscription!),
                _ => "❌ <b>Сталася невідома помилка.</b>"
            };

            await _telegram.SendMessageAsync(chatId, message, cancellationToken);
        }

        private static string BuildAlreadyActiveMessage(Subscription subscription)
        {
            var safeName = WebUtility.HtmlEncode(subscription.ProductName);
            return $"ℹ️ <b>Ця підписка вже існує у Вашому списку.</b>\n\n" +
                   $"📦 <b>{safeName}</b>\n" +
                   $"💰 <b>Поточна ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH\n\n" +
                   $"🔗 <a href=\"{subscription.ProductUrl}\">Перейти до товару</a>";
        }

        private static string BuildConfirmationMessage(Subscription subscription)
        {
            var safeName = WebUtility.HtmlEncode(subscription.ProductName);
            return $"✅ <b>Підписку створено!</b>\n\n" +
                   $"📦 <b>{safeName}</b>\n" +
                   $"🏪 <b>Магазин:</b> {subscription.StoreType.GetDescription()}\n" +
                   $"💰 <b>Ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH\n\n" +
                   $"🔗 <a href=\"{subscription.ProductUrl}\">Перейти до товару</a>";
        }
    }
}
