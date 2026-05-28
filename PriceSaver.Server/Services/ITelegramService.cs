using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Services
{
    public interface ITelegramService
    {
        Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
        Task SendMessageWithKeyboardAsync(long chatId, string text, IReplyMarkup replyMarkup, CancellationToken cancellationToken = default);
        Task SendMessageWithInlineButtonAsync(long chatId, string text, string buttonLabel, string callbackData, CancellationToken cancellationToken = default);
        Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default);
        Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default);
    }
}
