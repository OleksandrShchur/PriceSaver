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
                text.Equals("📋 Manage subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                await _subscriptionHandler.SendSubscriptionsAsync(chatId, cancellationToken);
                return;
            }

            if (text.Equals("❓ Instructions", StringComparison.OrdinalIgnoreCase))
            {
                await SendInstructionsAsync(chatId, cancellationToken);
                return;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                await _subscriptionHandler.CreateSubscriptionAsync(chatId, message.From?.Username, uri.ToString(), cancellationToken);
                return;
            }

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                "📌 Send a direct product link from ATB, Silpo, Metro, or Epicentr to track its price.",
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
                await _telegram.AnswerCallbackQueryAsync(callbackQuery.Id, "An error occurred.", true, cancellationToken);
            }
        }

        private async Task SendWelcomeMessageAsync(long chatId, CancellationToken cancellationToken)
        {
            var welcomeText = $"👋 Welcome to {_options.BotDisplayName}!\n\n" +
                            "📌 *How to use:*\n" +
                            "1. Send a product link from ATB, Silpo, Metro, or Epicentr\n" +
                            "2. The bot will track price changes for you\n" +
                            "3. Use the buttons below to manage your subscriptions\n\n" +
                            "📦 You can have up to " + _options.MaxSubscriptionsPerUser + " active subscriptions.";

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                welcomeText,
                GetMainKeyboard(),
                cancellationToken);
        }

        private async Task SendInstructionsAsync(long chatId, CancellationToken cancellationToken)
        {
            var instructionsText = "❓ *Supported Stores:*\n\n" +
                                 "🏪 ATB - https://www.atb.ua\n" +
                                 "🏪 Silpo - https://www.silpo.ua\n" +
                                 "🏪 Metro - https://www.metro.ua\n" +
                                 "🏪 Epicentr - https://www.epicentr.ua\n\n" +
                                 "*How to:*\n" +
                                 "• Send any product link to start tracking\n" +
                                 "• Use 📋 *Manage subscriptions* to view your tracked items\n" +
                                 "• Click 🗑️ *Remove* button to delete a subscription\n" +
                                 "Questions? Just send a product link! 🚀";

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
                    new[] { new KeyboardButton("📋 Manage subscriptions"), new KeyboardButton("❓ Instructions") }
                })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }
    }
}
