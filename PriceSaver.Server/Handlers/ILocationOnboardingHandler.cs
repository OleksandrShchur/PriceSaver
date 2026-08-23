using Telegram.Bot.Types;

namespace PriceSaver.Server.Handlers
{
    public interface ILocationOnboardingHandler
    {
        Task PromptForLocationAsync(long chatId, bool includeWelcomeBackHint, CancellationToken cancellationToken);

        Task HandleSharedLocationAsync(long chatId, Location location, CancellationToken cancellationToken);

        Task HandleTypedLocationAsync(long chatId, string text, CancellationToken cancellationToken);

        Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken);

        bool IsLocationCallback(string? data);
    }
}
