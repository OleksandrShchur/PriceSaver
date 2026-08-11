using System.Text;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Helpers;
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
        internal const int PageSize = 10;
        private const int NumberButtonsPerRow = 5;

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

                var sent = await _telegram.SendRichMessageAsync(
                    chatId,
                    BuildSubscriptionsListMarkdown(subscriptions, page: 0),
                    BuildListKeyboard(subscriptions, page: 0),
                    cancellationToken);

                if (!sent)
                {
                    await _telegram.SendMessageAsync(
                        chatId,
                        "⚠️ Для перегляду підписок потрібна актуальна версія Telegram.\n" +
                        "Будь ласка, <b>оновіть Telegram</b>, щоб користуватися всіма функціями бота.",
                        cancellationToken);
                    return;
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

        public async Task HandleSelectSubscriptionCallbackAsync(
            long chatId,
            string callbackQueryId,
            int page,
            string subscriptionId,
            int messageId,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Некоректний Id підписки.", true, cancellationToken);
                    return;
                }

                var subscriptions = await _subscriptionService.GetActiveSubscriptionsAsync(chatId, cancellationToken);
                var subscription = subscriptions.FirstOrDefault(s => s.Id == subscriptionGuid);
                if (subscription is null)
                {
                    await _telegram.AnswerCallbackQueryAsync(callbackQueryId, "Підписку не знайдено.", true, cancellationToken);
                    await ShowListPageAsync(chatId, messageId, subscriptions, page, cancellationToken);
                    return;
                }

                var safePage = ClampPage(page, subscriptions.Count);
                await _telegram.EditRichMessageAsync(
                    chatId,
                    messageId,
                    BuildDetailMarkdown(subscription),
                    BuildDetailKeyboard(subscription, safePage),
                    cancellationToken);

                await _telegram.AnswerCallbackQueryAsync(callbackQueryId, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in /{Command} handler for UserId: {UserId}", "sub_sel", chatId);
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQueryId,
                    "❌ Сталася непередбачена помилка. Спробуйте пізніше або зверніться до підтримки.",
                    true,
                    cancellationToken);
            }
        }

        public async Task HandleListPageCallbackAsync(
            long chatId,
            string callbackQueryId,
            int page,
            int messageId,
            CancellationToken cancellationToken)
        {
            try
            {
                var subscriptions = await _subscriptionService.GetActiveSubscriptionsAsync(chatId, cancellationToken);
                await ShowListPageAsync(chatId, messageId, subscriptions, page, cancellationToken);
                await _telegram.AnswerCallbackQueryAsync(callbackQueryId, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in /{Command} handler for UserId: {UserId}", "sub_list", chatId);
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQueryId,
                    "❌ Сталася непередбачена помилка. Спробуйте пізніше або зверніться до підтримки.",
                    true,
                    cancellationToken);
            }
        }

        public async Task HandleRemoveSubscriptionCallbackAsync(
            long chatId,
            string callbackQueryId,
            int page,
            string subscriptionId,
            int messageId,
            CancellationToken cancellationToken)
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

                if (messageId <= 0)
                {
                    return;
                }

                var subscriptions = await _subscriptionService.GetActiveSubscriptionsAsync(chatId, cancellationToken);
                await ShowListPageAsync(chatId, messageId, subscriptions, page, cancellationToken);
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

        public async Task HandleToggleNotifyOnIncreaseCallbackAsync(
            long chatId,
            string callbackQueryId,
            int page,
            string subscriptionId,
            int messageId,
            CancellationToken cancellationToken)
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
                    await _telegram.EditRichMessageAsync(
                        chatId,
                        messageId,
                        BuildDetailMarkdown(result.Subscription),
                        BuildDetailKeyboard(result.Subscription, page),
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

        private async Task ShowListPageAsync(
            long chatId,
            int messageId,
            IReadOnlyList<Subscription> subscriptions,
            int page,
            CancellationToken cancellationToken)
        {
            if (subscriptions.Count == 0)
            {
                await _telegram.EditRichMessageAsync(
                    chatId,
                    messageId,
                    "⚠️ **У Вас немає активних підписок.**",
                    new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()),
                    cancellationToken);
                return;
            }

            var safePage = ClampPage(page, subscriptions.Count);
            await _telegram.EditRichMessageAsync(
                chatId,
                messageId,
                BuildSubscriptionsListMarkdown(subscriptions, safePage),
                BuildListKeyboard(subscriptions, safePage),
                cancellationToken);
        }

        internal static string BuildSubscriptionsListMarkdown(IReadOnlyList<Subscription> subscriptions, int page)
        {
            var safePage = ClampPage(page, subscriptions.Count);
            var start = safePage * PageSize;
            var endExclusive = Math.Min(start + PageSize, subscriptions.Count);
            var sb = new StringBuilder();

            sb.AppendLine($"📋 **Мої підписки** ({start + 1}–{endExclusive} з {subscriptions.Count})");
            sb.AppendLine();
            sb.AppendLine("| # | Товар | Магазин | Ціна | ↑ |");
            sb.AppendLine("|:-:|:------|:--------|-----:|:-:|");

            for (var i = start; i < endExclusive; i++)
            {
                var subscription = subscriptions[i];
                var number = i + 1;
                var product = RichMarkdown.FormatProductLink(
                    subscription.ProductName ?? string.Empty,
                    subscription.ProductUrl ?? string.Empty);
                var store = RichMarkdown.EscapeTableCell(subscription.StoreType.GetDescription());
                var notifyIcon = subscription.NotifyOnIncrease ? "🔔" : "🔕";

                sb.AppendLine(
                    $"| {number} | {product} | {store} | {subscription.CurrentPrice:0.##} | {notifyIcon} |");
            }

            sb.AppendLine();
            sb.Append("Натисніть номер товару для керування.");
            return sb.ToString();
        }

        internal static InlineKeyboardMarkup BuildListKeyboard(IReadOnlyList<Subscription> subscriptions, int page)
        {
            var safePage = ClampPage(page, subscriptions.Count);
            var start = safePage * PageSize;
            var countOnPage = Math.Min(PageSize, subscriptions.Count - start);
            var rows = new List<InlineKeyboardButton[]>();

            for (var offset = 0; offset < countOnPage; offset += NumberButtonsPerRow)
            {
                var rowCount = Math.Min(NumberButtonsPerRow, countOnPage - offset);
                var row = new InlineKeyboardButton[rowCount];
                for (var i = 0; i < rowCount; i++)
                {
                    var index = start + offset + i;
                    var subscription = subscriptions[index];
                    var label = (index + 1).ToString();
                    row[i] = InlineKeyboardButton.WithCallbackData(label, $"sub_sel_{safePage}_{subscription.Id}");
                }

                rows.Add(row);
            }

            var totalPages = GetTotalPages(subscriptions.Count);
            if (totalPages > 1)
            {
                var nav = new List<InlineKeyboardButton>();
                if (safePage > 0)
                {
                    nav.Add(InlineKeyboardButton.WithCallbackData("◀", $"sub_list_{safePage - 1}"));
                }

                nav.Add(InlineKeyboardButton.WithCallbackData($"{safePage + 1}/{totalPages}", $"sub_list_{safePage}"));

                if (safePage < totalPages - 1)
                {
                    nav.Add(InlineKeyboardButton.WithCallbackData("▶", $"sub_list_{safePage + 1}"));
                }

                rows.Add(nav.ToArray());
            }

            return new InlineKeyboardMarkup(rows);
        }

        internal static string BuildDetailMarkdown(Subscription subscription)
        {
            var product = RichMarkdown.FormatProductLink(
                subscription.ProductName ?? string.Empty,
                subscription.ProductUrl ?? string.Empty);
            var store = RichMarkdown.EscapeTableCell(subscription.StoreType.GetDescription());
            var notifyState = subscription.NotifyOnIncrease
                ? "🔔 Сповіщення про здорожчання увімкнено"
                : "🔕 Сповіщення про здорожчання вимкнено";

            return $"📦 {product}\n\n" +
                   $"🏪 **Магазин:** {store}\n" +
                   $"💰 **Ціна:** `{subscription.CurrentPrice:0.##}` UAH\n" +
                   $"{notifyState}";
        }

        internal static InlineKeyboardMarkup BuildDetailKeyboard(Subscription subscription, int page)
        {
            var notifyButtonText = subscription.NotifyOnIncrease
                ? "🔕 Не сповіщати про здорожчання"
                : "🔔 Сповіщати про здорожчання";

            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        notifyButtonText,
                        $"sub_toggle_increase_{page}_{subscription.Id}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        "🗑️ Видалити",
                        $"sub_remove_{page}_{subscription.Id}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⬅️ Назад", $"sub_list_{page}")
                }
            });
        }

        private static string BuildAlreadyActiveMessage(Subscription subscription)
        {
            var safeName = System.Net.WebUtility.HtmlEncode(subscription.ProductName);
            var safeStoreDescription = System.Net.WebUtility.HtmlEncode(subscription.StoreType.GetDescription());
            var safeProductUrl = System.Net.WebUtility.HtmlEncode(subscription.ProductUrl);
            return $"ℹ️ <b>Ця підписка вже існує у Вашому списку.</b>\n\n" +
                   $"📦 <a href=\"{safeProductUrl}\"><b>{safeName}</b></a>\n" +
                   $"🏪 <b>Магазин:</b> {safeStoreDescription}\n" +
                   $"💰 <b>Поточна ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH";
        }

        private static string BuildConfirmationMessage(Subscription subscription)
        {
            var safeName = System.Net.WebUtility.HtmlEncode(subscription.ProductName);
            var safeStoreDescription = System.Net.WebUtility.HtmlEncode(subscription.StoreType.GetDescription());
            var safeProductUrl = System.Net.WebUtility.HtmlEncode(subscription.ProductUrl);
            return $"✅ <b>Підписку створено!</b>\n\n" +
                   $"📦 <a href=\"{safeProductUrl}\"><b>{safeName}</b></a>\n" +
                   $"🏪 <b>Магазин:</b> {safeStoreDescription}\n" +
                   $"💰 <b>Ціна:</b> <code>{subscription.CurrentPrice:0.##}</code> UAH";
        }

        internal static int ClampPage(int page, int totalCount)
        {
            if (totalCount <= 0)
            {
                return 0;
            }

            var totalPages = GetTotalPages(totalCount);
            if (page < 0)
            {
                return 0;
            }

            if (page >= totalPages)
            {
                return totalPages - 1;
            }

            return page;
        }

        private static int GetTotalPages(int totalCount) =>
            totalCount <= 0 ? 1 : (totalCount + PageSize - 1) / PageSize;

        /// <summary>
        /// Parses <c>{page}_{guid}</c> from callback payloads such as <c>sub_sel_0_{guid}</c>.
        /// </summary>
        public static bool TryParsePagedSubscriptionCallback(string payload, out int page, out string subscriptionId)
        {
            page = 0;
            subscriptionId = string.Empty;

            var separator = payload.IndexOf('_');
            if (separator <= 0 || separator == payload.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(payload[..separator], out page))
            {
                return false;
            }

            subscriptionId = payload[(separator + 1)..];
            return !string.IsNullOrWhiteSpace(subscriptionId);
        }
    }
}
