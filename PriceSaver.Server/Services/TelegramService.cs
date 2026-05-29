using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Services
{
    public class TelegramService : ITelegramService
    {
        private readonly TelegramOptions _options;
        private readonly ILogger<TelegramService> _logger;
        private readonly Lazy<ITelegramBotClient?> _client;

        public TelegramService(IOptions<TelegramOptions> options, ILogger<TelegramService> logger)
        {
            _options = options.Value;
            _logger = logger;
            _client = new Lazy<ITelegramBotClient?>(CreateClient);
        }

        public ITelegramBotClient? Client => _client.Value;

        public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; message to chat {ChatId} was skipped.", chatId);

                return;
            }

            await Client.SendTextMessageAsync(chatId, text, cancellationToken: cancellationToken, parseMode: ParseMode.Html);
        }

        public async Task SendMessageWithKeyboardAsync(long chatId, string text, IReplyMarkup replyMarkup, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; message to chat {ChatId} was skipped.", chatId);

                return;
            }

            await Client.SendTextMessageAsync(chatId, text, replyMarkup: replyMarkup, cancellationToken: cancellationToken,
                parseMode: ParseMode.Html);
        }

        public async Task SendMessageWithInlineButtonAsync(long chatId, string text, string buttonLabel, string callbackData, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; message to chat {ChatId} was skipped.", chatId);

                return;
            }

            var inlineKeyboard = new InlineKeyboardMarkup(
                new[] { InlineKeyboardButton.WithCallbackData(buttonLabel, callbackData) });

            await Client.SendTextMessageAsync(chatId, text, replyMarkup: inlineKeyboard, cancellationToken: cancellationToken,
                parseMode: ParseMode.Html);
        }

        public async Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; message deletion for chat {ChatId} was skipped.", chatId);

                return;
            }

            try
            {
                await Client.DeleteMessageAsync(chatId, messageId, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete message {MessageId} from chat {ChatId}.", messageId, chatId);
            }
        }

        public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; callback query answer was skipped.");

                return;
            }

            await Client.AnswerCallbackQueryAsync(callbackQueryId, text, showAlert, cancellationToken: cancellationToken);
        }

        private ITelegramBotClient? CreateClient()
        {
            if (string.IsNullOrWhiteSpace(_options.BotToken))
            {
                return null;
            }

            return new TelegramBotClient(_options.BotToken);
        }
    }
}
