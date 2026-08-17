using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Services
{
    public interface ITelegramService
    {
        Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a Telegram rich message (Bot API 10.1+). Returns <c>true</c> when the API accepts the message.
        /// </summary>
        Task<bool> SendRichMessageAsync(
            long chatId,
            string markdown,
            IReplyMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default);

        Task SendMessageWithKeyboardAsync(long chatId, string text, IReplyMarkup replyMarkup, CancellationToken cancellationToken = default);
        Task SendMessageWithInlineButtonAsync(long chatId, string text, string buttonLabel, string callbackData, CancellationToken cancellationToken = default);
        Task EditMessageTextAsync(long chatId, int messageId, string text, InlineKeyboardMarkup replyMarkup, CancellationToken cancellationToken = default);

        /// <summary>
        /// Edits an existing message using rich markdown content (Bot API 10.1+).
        /// Returns <c>true</c> when the API accepts the edit.
        /// </summary>
        Task<bool> EditRichMessageAsync(
            long chatId,
            int messageId,
            string markdown,
            InlineKeyboardMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default);

        Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default);
        Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default);
    }
}
