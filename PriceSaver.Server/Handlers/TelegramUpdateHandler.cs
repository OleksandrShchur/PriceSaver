using System.Net;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Handlers
{
    public class TelegramUpdateHandler : ITelegramUpdateHandler
    {
        private readonly ITelegramService _telegram;
        private readonly TelegramOptions _options;
        private readonly IUserService _userService;
        private readonly ISubscriptionHandler _subscriptionHandler;
        private readonly ILogger<TelegramUpdateHandler> _logger;

        public TelegramUpdateHandler(
            ITelegramService telegram,
            IOptions<TelegramOptions> options,
            IUserService userService,
            ISubscriptionHandler subscriptionHandler,
            ILogger<TelegramUpdateHandler> logger)
        {
            _telegram = telegram;
            _options = options.Value;
            _userService = userService;
            _subscriptionHandler = subscriptionHandler;
            _logger = logger;
        }

        public async Task HandleAsync(Update update, CancellationToken cancellationToken = default)
        {
            // Handle callback queries (button clicks)
            if (update.CallbackQuery is { Data: not null } callbackQuery)
            {
                await HandleCallbackQueryAsync(callbackQuery, cancellationToken);

                return;
            }

            if (update.Message is not { Type: MessageType.Text } message)
            {
                return;
            }

            var chatId = message.Chat.Id;
            var text = message.Text?.Trim() ?? string.Empty;

            if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
            {
                await _userService.EnsureUserExistsAsync(chatId, message.From?.Username, cancellationToken);
                await SendWelcomeMessageAsync(chatId, cancellationToken);

                return;
            }

            if (text.StartsWith("/my_subscriptions", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("📋 Мої підписки", StringComparison.OrdinalIgnoreCase))
            {
                await _subscriptionHandler.SendSubscriptionsAsync(chatId, cancellationToken);

                return;
            }

            if (text.StartsWith("/instructions", StringComparison.OrdinalIgnoreCase) || 
                text.Equals("❓ Інструкції", StringComparison.OrdinalIgnoreCase))
            {
                await SendInstructionsAsync(chatId, cancellationToken);

                return;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                await _telegram.SendMessageAsync(
                    chatId,
                    "🔍 <i>Перевіряємо посилання, зачекайте хвилинку...</i>",
                    cancellationToken);

                await _subscriptionHandler.CreateSubscriptionAsync(chatId, message.From?.Username, uri.ToString(), cancellationToken);
                
                return;
            }

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                "📌 <b>Надішліть пряме посилання</b> на продукт з ATB, щоб почати відстежувати його ціну.",
                GetMainKeyboard(),
                cancellationToken);
        }

        private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            try
            {
                if (callbackQuery.Data?.StartsWith("sub_remove_") == true)
                {
                    var subscriptionId = callbackQuery.Data["sub_remove_".Length..];
                    var messageId = callbackQuery.Message?.MessageId ?? 0;

                    await _subscriptionHandler.HandleRemoveSubscriptionCallbackAsync(
                        callbackQuery.From.Id,
                        callbackQuery.Id,
                        subscriptionId,
                        messageId,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle callback query {CallbackData}", callbackQuery.Data);
                await _telegram.AnswerCallbackQueryAsync(callbackQuery.Id, "Сталася помилка.", true, cancellationToken);
            }
        }

        private async Task SendWelcomeMessageAsync(long chatId, CancellationToken cancellationToken)
        {
            var safeBotName = WebUtility.HtmlEncode(_options.BotDisplayName);
            var welcomeText = $"👋 Ласкаво просимо до <b>{safeBotName}</b>!\n\n" +
                              "📌 <b>Як користуватися:</b>\n" +
                              "1️⃣ Надішліть посилання на продукт з АТБ або Сільпо\n" +
                              "2️⃣ Бот автоматично відстежуватиме зміни його ціни\n" +
                              "3️⃣ Використовуйте меню нижче для керування вашими підписками\n\n" +
                              $"📦 Ви можете мати до <code>{_options.MaxSubscriptionsPerUser}</code> активних підписок одночасно.";

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                welcomeText,
                GetMainKeyboard(),
                cancellationToken: cancellationToken);
        }

        private async Task SendInstructionsAsync(long chatId, CancellationToken cancellationToken)
        {
            var instructionsText = "❓ <b>Підтримувані магазини:</b>\n\n" +
                                   "🏪 <a href=\"https://www.atbmarket.com/\">АТБ Маркет</a>\n\n" +
                                   "🏪 <a href=\"https://silpo.ua/\">Сільпо</a>\n\n" +
                                   "📌 <b>Інструкція користувача:</b>\n" +
                                   "• Надішліть будь-яке пряме посилання на товар, щоб увімкнути моніторинг\n" +
                                   "• Натисніть 📋 <b>Мої підписки</b>, щоб побачити список ваших товарів\n" +
                                   "• Використовуйте кнопку 🗑️ <b>Видалити</b> під товаром, щоб скасувати стеження\n\n" +
                                   "🚀 <i>Залишилися питання? Просто надішліть посилання на товар!</i>";

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                instructionsText,
                GetMainKeyboard(),
                cancellationToken: cancellationToken);
        }

        private static IReplyMarkup GetMainKeyboard()
        {
            return new ReplyKeyboardMarkup(
                new[]
                {
                    new[] { new KeyboardButton("📋 Мої підписки") },
                    new[] { new KeyboardButton("❓ Інструкції") }
                })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }
    }
}
