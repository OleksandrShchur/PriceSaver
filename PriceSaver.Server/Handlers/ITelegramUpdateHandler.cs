using Telegram.Bot.Types;

namespace PriceSaver.Server.Handlers
{
    public interface ITelegramUpdateHandler
    {
        Task HandleAsync(Update update, CancellationToken cancellationToken = default);
    }
}
