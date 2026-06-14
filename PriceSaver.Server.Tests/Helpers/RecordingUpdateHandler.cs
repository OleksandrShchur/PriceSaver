using System.Collections.Concurrent;
using PriceSaver.Server.Handlers;
using Telegram.Bot.Types;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// Records the updates routed to it, used to assert that the Telegram
    /// controller deserializes payloads and delegates to the handler.
    /// </summary>
    public sealed class RecordingUpdateHandler : ITelegramUpdateHandler
    {
        public ConcurrentQueue<Update> Handled { get; } = new();

        public Task HandleAsync(Update update, CancellationToken cancellationToken = default)
        {
            Handled.Enqueue(update);
            return Task.CompletedTask;
        }
    }
}
