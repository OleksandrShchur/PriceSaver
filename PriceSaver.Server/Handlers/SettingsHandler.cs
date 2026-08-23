using System.Globalization;
using System.Net;
using PriceSaver.Server.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using User = PriceSaver.Server.Models.User;

namespace PriceSaver.Server.Handlers
{
    public class SettingsHandler : ISettingsHandler
    {
        public const string SettingsButtonText = "⚙️ Налаштування";
        public const string CallbackChangeLocation = "settings_change_location";

        private readonly ITelegramService _telegram;
        private readonly ILocationOnboardingHandler _locationOnboarding;

        public SettingsHandler(ITelegramService telegram, ILocationOnboardingHandler locationOnboarding)
        {
            _telegram = telegram;
            _locationOnboarding = locationOnboarding;
        }

        public bool IsSettingsCallback(string? data) =>
            data is not null &&
            data.Equals(CallbackChangeLocation, StringComparison.Ordinal);

        public async Task SendSettingsAsync(long chatId, User user, CancellationToken cancellationToken)
        {
            var locationLine = FormatLocationLine(user);
            var text =
                "⚙️ <b>Налаштування</b>\n\n" +
                $"📍 <b>Локація:</b> {locationLine}";

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                text,
                BuildSettingsKeyboard(),
                cancellationToken);
        }

        public async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.From.Id;

            if (callbackQuery.Data != CallbackChangeLocation)
            {
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "Некоректні дані кнопки.",
                    showAlert: true,
                    cancellationToken);
                return;
            }

            await _telegram.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
            await _locationOnboarding.PromptForLocationAsync(
                chatId,
                includeWelcomeBackHint: false,
                cancellationToken);
        }

        private static string FormatLocationLine(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.LocationName))
            {
                return WebUtility.HtmlEncode(user.LocationName);
            }

            if (user.Latitude is not null && user.Longitude is not null)
            {
                var lat = user.Latitude.Value.ToString("0.######", CultureInfo.InvariantCulture);
                var lon = user.Longitude.Value.ToString("0.######", CultureInfo.InvariantCulture);
                return $"<code>{lat}, {lon}</code>";
            }

            return "не вказано";
        }

        private static InlineKeyboardMarkup BuildSettingsKeyboard()
        {
            return new InlineKeyboardMarkup(
            [
                [
                    InlineKeyboardButton.WithCallbackData(
                        "📍 Змінити локацію",
                        CallbackChangeLocation)
                ]
            ]);
        }
    }
}
