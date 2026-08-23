using Telegram.Bot.Types;
using User = PriceSaver.Server.Models.User;

namespace PriceSaver.Server.Handlers
{
    public interface ISettingsHandler
    {
        Task SendSettingsAsync(long chatId, User user, CancellationToken cancellationToken);

        Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken);

        bool IsSettingsCallback(string? data);
    }
}
