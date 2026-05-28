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

            if (text.Equals("❓ Інструкції", StringComparison.OrdinalIgnoreCase))
            {
                await SendInstructionsAsync(chatId, cancellationToken);

                return;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                await _telegram.SendMessageAsync(chatId, "🔍 Перевіряємо посилання...", cancellationToken);
                await _subscriptionHandler.CreateSubscriptionAsync(chatId, message.From?.Username, uri.ToString(), cancellationToken);
                
                return;
            }

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                "📌 Надішліть пряме посилання на продукт з ATB, щоб відстежувати його ціну.",
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
            var welcomeText = $"👋 Ласкаво просимо до {_options.BotDisplayName}!\n\n" +
                            "📌 *Як користуватися:*\n" +
                            "1. Надішліть посилання на продукт з ATB\n" +
                            "2. Бот буде відстежувати зміни цін для вас\n" +
                            "3. Використовуйте кнопки нижче, щоб керувати своїми підписками\n\n" +
                            "📦 Ви можете мати до " + _options.MaxSubscriptionsPerUser + " активних підписок.";

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                welcomeText,
                GetMainKeyboard(),
                cancellationToken);
        }

        private async Task SendInstructionsAsync(long chatId, CancellationToken cancellationToken)
        {
            var instructionsText = "❓ *Підтримувані магазини:*\n\n" +
                                 "🏪 ATБ - https://www.atbmarket.com/\n\n" +
                                 "*Як користуватися:*\n" +
                                 "• Надішліть будь-яке посилання на продукт, щоб почати відстеження\n" +
                                 "• Використовуйте 📋 *Мої підписки*, щоб переглянути ваші відстежувані товари\n" +
                                 "• Натисніть 🗑️ *Видалити*, щоб видалити підписку\n" +
                                 "Питання? Просто надішліть посилання на продукт! 🚀";
            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                instructionsText,
                GetMainKeyboard(),
                cancellationToken);
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
