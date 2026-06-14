using System.Collections.Concurrent;
using PriceSaver.Server.Services;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// In-memory <see cref="ITelegramService"/> that records every outbound
    /// interaction so integration tests can assert on bot behaviour.
    /// </summary>
    public sealed class RecordingTelegramService : ITelegramService
    {
        public record SentMessage(long ChatId, string Text);
        public record InlineButton(long ChatId, string Text, string ButtonLabel, string CallbackData);
        public record DeletedMessage(long ChatId, int MessageId);
        public record CallbackAnswer(string CallbackQueryId, string? Text, bool ShowAlert);

        public ConcurrentQueue<SentMessage> Messages { get; } = new();
        public ConcurrentQueue<SentMessage> KeyboardMessages { get; } = new();
        public ConcurrentQueue<InlineButton> InlineButtons { get; } = new();
        public ConcurrentQueue<DeletedMessage> DeletedMessages { get; } = new();
        public ConcurrentQueue<CallbackAnswer> CallbackAnswers { get; } = new();

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            Messages.Enqueue(new SentMessage(chatId, text));
            return Task.CompletedTask;
        }

        public Task SendMessageWithKeyboardAsync(long chatId, string text, IReplyMarkup replyMarkup, CancellationToken cancellationToken = default)
        {
            KeyboardMessages.Enqueue(new SentMessage(chatId, text));
            return Task.CompletedTask;
        }

        public Task SendMessageWithInlineButtonAsync(long chatId, string text, string buttonLabel, string callbackData, CancellationToken cancellationToken = default)
        {
            InlineButtons.Enqueue(new InlineButton(chatId, text, buttonLabel, callbackData));
            return Task.CompletedTask;
        }

        public Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default)
        {
            DeletedMessages.Enqueue(new DeletedMessage(chatId, messageId));
            return Task.CompletedTask;
        }

        public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default)
        {
            CallbackAnswers.Enqueue(new CallbackAnswer(callbackQueryId, text, showAlert));
            return Task.CompletedTask;
        }
    }
}
