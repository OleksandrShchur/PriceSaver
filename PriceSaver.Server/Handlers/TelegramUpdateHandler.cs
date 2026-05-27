using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PriceSaver.Server.Handlers
{
    public class TelegramUpdateHandler : ITelegramUpdateHandler
    {
        private readonly ITelegramService _telegram;
        private readonly TelegramOptions _options;
        private readonly IUserService _userService;
        private readonly ISubscriptionHandler _subscriptionHandler;

        public TelegramUpdateHandler(
            ITelegramService telegram,
            IOptions<TelegramOptions> options,
            IUserService userService,
            ISubscriptionHandler subscriptionHandler)
        {
            _telegram = telegram;
            _options = options.Value;
            _userService = userService;
            _subscriptionHandler = subscriptionHandler;
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
                await _userService.EnsureUserExistsAsync(chatId, message.From?.Username, cancellationToken);
                await _telegram.SendMessageAsync(
                    chatId,
                    $"Welcome to {_options.BotDisplayName}. Send a supported store product link to create a price subscription. Use /my_subscriptions to view them.",
                    cancellationToken);
                return;
            }

            if (text.StartsWith("/my_subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                await _subscriptionHandler.SendSubscriptionsAsync(chatId, cancellationToken);
                return;
            }

            if (text.StartsWith("/delete_subscription", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("/delete", StringComparison.OrdinalIgnoreCase))
            {
                await _subscriptionHandler.DeleteSubscriptionAsync(chatId, text, cancellationToken);
                return;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                await _subscriptionHandler.CreateSubscriptionAsync(chatId, message.From?.Username, uri.ToString(), cancellationToken);
                return;
            }

            await _telegram.SendMessageAsync(
                chatId,
                "Send a direct product link from ATB, Silpo, Metro, or Epicentr. Use /my_subscriptions to manage saved products.",
                cancellationToken);
        }
    }
}
