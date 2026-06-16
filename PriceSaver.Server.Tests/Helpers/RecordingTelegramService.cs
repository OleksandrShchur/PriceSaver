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
        public record RichMessage(long ChatId, string Markdown);
        public record EditedMessage(long ChatId, int MessageId, string Text);
        public record InlineButton(long ChatId, string Text, string ButtonLabel, string CallbackData);
        public record DeletedMessage(long ChatId, int MessageId);
        public record CallbackAnswer(string CallbackQueryId, string? Text, bool ShowAlert);

        public ConcurrentQueue<SentMessage> Messages { get; } = new();
        public ConcurrentQueue<RichMessage> RichMessages { get; } = new();
        public ConcurrentQueue<EditedMessage> EditedMessages { get; } = new();
        public ConcurrentQueue<SentMessage> KeyboardMessages { get; } = new();
        public ConcurrentQueue<InlineButton> InlineButtons { get; } = new();
        public ConcurrentQueue<DeletedMessage> DeletedMessages { get; } = new();
        public ConcurrentQueue<CallbackAnswer> CallbackAnswers { get; } = new();

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            Messages.Enqueue(new SentMessage(chatId, text));
            return Task.CompletedTask;
        }

        public Task SendRichMessageAsync(long chatId, string markdown, CancellationToken cancellationToken = default)
        {
            RichMessages.Enqueue(new RichMessage(chatId, markdown));
            return Task.CompletedTask;
        }

        public Task SendMessageWithKeyboardAsync(long chatId, string text, IReplyMarkup replyMarkup, CancellationToken cancellationToken = default)
        {
            KeyboardMessages.Enqueue(new SentMessage(chatId, text));

            // Some parts of the bot send inline buttons through the generic keyboard method.
            // Record them so integration tests can assert on callback data.
            if (replyMarkup is InlineKeyboardMarkup inlineKeyboard)
            {
                foreach (var row in inlineKeyboard.InlineKeyboard)
                {
                    foreach (var button in row)
                    {
                        if (!string.IsNullOrWhiteSpace(button.CallbackData))
                        {
                            InlineButtons.Enqueue(new InlineButton(chatId, text, button.Text, button.CallbackData));
                        }
                    }
                }
            }

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

        public Task EditMessageTextAsync(long chatId, int messageId, string text, InlineKeyboardMarkup replyMarkup, CancellationToken cancellationToken = default)
        {
            EditedMessages.Enqueue(new EditedMessage(chatId, messageId, text));
            return Task.CompletedTask;
        }

        public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default)
        {
            CallbackAnswers.Enqueue(new CallbackAnswer(callbackQueryId, text, showAlert));
            return Task.CompletedTask;
        }
    }
}
