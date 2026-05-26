using Telegram.Bot.Types;

namespace PriceSaver.Server.Services
{
    public interface ITelegramUpdateHandler
    {
        Task HandleAsync(Update update, CancellationToken cancellationToken = default);
    }
}
