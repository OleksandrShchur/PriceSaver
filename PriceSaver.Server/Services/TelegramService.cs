using System.Net.Http;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Services
{
    public class TelegramService : ITelegramService
    {
        private static readonly HttpClient HttpClient = new();
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

            await Client.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                parseMode: ParseMode.Html,
                disableWebPagePreview: true,
                cancellationToken: cancellationToken);
        }

        public async Task SendRichMessageAsync(long chatId, string markdown, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; rich message to chat {ChatId} was skipped.", chatId);
                return;
            }

            // Telegram Rich Messages (Bot API 10.1+) are not covered by our current typed wrapper,
            // so we call the REST endpoint directly.
            var url = $"https://api.telegram.org/bot{_options.BotToken}/sendRichMessage";

            var payload = new Dictionary<string, object?>
            {
                ["chat_id"] = chatId,
                ["disable_web_page_preview"] = true,
                ["rich_message"] = new Dictionary<string, object?>
                {
                    ["markdown"] = markdown
                }
            };

            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                using var response = await HttpClient.PostAsync(url, content, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("sendRichMessage failed for chat {ChatId}. HTTP {StatusCode}. Body: {Body}",
                        chatId, response.StatusCode, responseText);
                    return;
                }

                using var json = JsonDocument.Parse(responseText);
                if (json.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
                {
                    return;
                }

                var description = json.RootElement.TryGetProperty("description", out var d) ? d.GetString() : null;
                _logger.LogWarning("sendRichMessage returned ok=false for chat {ChatId}. Description: {Description}", chatId, description);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send rich message to chat {ChatId}.", chatId);
            }
        }

        public async Task SendMessageWithKeyboardAsync(long chatId, string text, IReplyMarkup replyMarkup, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; message to chat {ChatId} was skipped.", chatId);
                
                return;
            }

            await Client.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                replyMarkup: replyMarkup,
                parseMode: ParseMode.Html,
                disableWebPagePreview: true,
                cancellationToken: cancellationToken);
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

            await Client.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                replyMarkup: inlineKeyboard,
                parseMode: ParseMode.Html,
                disableWebPagePreview: true,
                cancellationToken: cancellationToken);
        }

        public async Task EditMessageTextAsync(long chatId, int messageId, string text, InlineKeyboardMarkup replyMarkup, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; message edit for chat {ChatId} was skipped.", chatId);

                return;
            }

            await Client.EditMessageTextAsync(
                chatId: chatId,
                messageId: messageId,
                text: text,
                parseMode: ParseMode.Html,
                disableWebPagePreview: true,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
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
