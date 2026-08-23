using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Handlers
{
    public class LocationOnboardingHandler : ILocationOnboardingHandler
    {
        public const string CallbackPickPrefix = "loc_pick_";
        public const string CallbackBack = "loc_back";
        public const string BackButtonText = "⬅️ Назад";
        public const string ShareLocationButtonText = "📍 Поділитися геолокацією";
        public const string SharedLocationFallbackName = "Поділена геолокація";

        private static readonly JsonSerializerOptions PayloadJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ITelegramService _telegram;
        private readonly IUserService _userService;
        private readonly INominatimGeocodingService _geocoding;
        private readonly TelegramOptions _telegramOptions;
        private readonly ILogger<LocationOnboardingHandler> _logger;

        public LocationOnboardingHandler(
            ITelegramService telegram,
            IUserService userService,
            INominatimGeocodingService geocoding,
            IOptions<TelegramOptions> telegramOptions,
            ILogger<LocationOnboardingHandler> logger)
        {
            _telegram = telegram;
            _userService = userService;
            _geocoding = geocoding;
            _telegramOptions = telegramOptions.Value;
            _logger = logger;
        }

        public bool IsLocationCallback(string? data) =>
            data is not null &&
            (data.StartsWith(CallbackPickPrefix, StringComparison.Ordinal) ||
             data.Equals(CallbackBack, StringComparison.Ordinal));

        public async Task PromptForLocationAsync(
            long chatId,
            bool includeWelcomeBackHint,
            CancellationToken cancellationToken)
        {
            await _userService.SetConversationStateAsync(
                chatId,
                ConversationStates.AwaitingLocation,
                payload: null,
                cancellationToken);

            var text = includeWelcomeBackHint
                ? "⬅️ Добре, почнімо з локації знову.\n\n" + BuildLocationPromptBody()
                : BuildLocationPromptBody();

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                text,
                GetLocationKeyboard(),
                cancellationToken);
        }

        public async Task HandleSharedLocationAsync(
            long chatId,
            Location location,
            CancellationToken cancellationToken)
        {
            var latitude = (decimal)location.Latitude;
            var longitude = (decimal)location.Longitude;

            await _telegram.SendMessageAsync(
                chatId,
                "🔍 <i>Визначаємо адресу, зачекайте…</i>",
                cancellationToken);

            var reverse = await _geocoding.ReverseAsync(latitude, longitude, cancellationToken);
            var name = reverse?.DisplayName ?? SharedLocationFallbackName;

            await SaveAndConfirmAsync(chatId, latitude, longitude, name, cancellationToken);
        }

        public async Task HandleTypedLocationAsync(
            long chatId,
            string text,
            CancellationToken cancellationToken)
        {
            var trimmed = text.Trim();
            if (trimmed.Equals(BackButtonText, StringComparison.OrdinalIgnoreCase))
            {
                await SendWelcomeMessageAsync(chatId, cancellationToken);
                await PromptForLocationAsync(chatId, includeWelcomeBackHint: false, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.Equals(ShareLocationButtonText, StringComparison.OrdinalIgnoreCase))
            {
                await PromptForLocationAsync(chatId, includeWelcomeBackHint: false, cancellationToken);
                return;
            }

            await _telegram.SendMessageAsync(
                chatId,
                "🔍 <i>Шукаємо локацію, зачекайте…</i>",
                cancellationToken);

            var results = await _geocoding.SearchAsync(trimmed, cancellationToken);
            if (results.Count == 0)
            {
                await _userService.SetConversationStateAsync(
                    chatId,
                    ConversationStates.AwaitingLocation,
                    payload: null,
                    cancellationToken);

                await _telegram.SendMessageWithKeyboardAsync(
                    chatId,
                    "❌ Локацію не знайдено. Спробуйте іншу назву або поділіться геолокацією кнопкою нижче.",
                    GetLocationKeyboard(),
                    cancellationToken);
                return;
            }

            if (results.Count == 1)
            {
                var only = results[0];
                await SaveAndConfirmAsync(
                    chatId,
                    only.Latitude,
                    only.Longitude,
                    only.DisplayName,
                    cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(results, PayloadJsonOptions);
            await _userService.SetConversationStateAsync(
                chatId,
                ConversationStates.AwaitingLocationConfirm,
                payload,
                cancellationToken);

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                "📍 Знайдено кілька варіантів. Оберіть потрібний:",
                BuildConfirmKeyboard(results),
                cancellationToken);
        }

        public async Task HandleCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var data = callbackQuery.Data;
            var chatId = callbackQuery.From.Id;

            if (data == CallbackBack)
            {
                await _telegram.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
                await PromptForLocationAsync(chatId, includeWelcomeBackHint: true, cancellationToken);
                return;
            }

            if (data?.StartsWith(CallbackPickPrefix, StringComparison.Ordinal) != true)
            {
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "Некоректні дані кнопки.",
                    showAlert: true,
                    cancellationToken);
                return;
            }

            if (!int.TryParse(data[CallbackPickPrefix.Length..], out var index) || index < 0)
            {
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "Некоректні дані кнопки.",
                    showAlert: true,
                    cancellationToken);
                return;
            }

            var user = await _userService.GetAsync(chatId, cancellationToken);
            if (user is null ||
                user.ConversationState != ConversationStates.AwaitingLocationConfirm ||
                string.IsNullOrWhiteSpace(user.ConversationPayload))
            {
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "Сесія вибору застаріла. Введіть локацію знову.",
                    showAlert: true,
                    cancellationToken);
                await PromptForLocationAsync(chatId, includeWelcomeBackHint: false, cancellationToken);
                return;
            }

            List<GeocodingResult>? candidates;
            try
            {
                candidates = JsonSerializer.Deserialize<List<GeocodingResult>>(
                    user.ConversationPayload,
                    PayloadJsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize location confirm payload for {ChatId}", chatId);
                candidates = null;
            }

            if (candidates is null || index >= candidates.Count)
            {
                await _telegram.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "Некоректний варіант.",
                    showAlert: true,
                    cancellationToken);
                await PromptForLocationAsync(chatId, includeWelcomeBackHint: false, cancellationToken);
                return;
            }

            var chosen = candidates[index];
            await _telegram.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
            await SaveAndConfirmAsync(
                chatId,
                chosen.Latitude,
                chosen.Longitude,
                chosen.DisplayName,
                cancellationToken);
        }

        private async Task SaveAndConfirmAsync(
            long chatId,
            decimal latitude,
            decimal longitude,
            string locationName,
            CancellationToken cancellationToken)
        {
            await _userService.SaveLocationAsync(chatId, latitude, longitude, locationName, cancellationToken);

            var safeName = WebUtility.HtmlEncode(locationName);
            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                $"✅ Локацію збережено: <b>{safeName}</b>\n\nТепер можете надсилати посилання на товари або користуватися меню.",
                GetMainKeyboard(),
                cancellationToken);
        }

        private async Task SendWelcomeMessageAsync(long chatId, CancellationToken cancellationToken)
        {
            var safeBotName = WebUtility.HtmlEncode(_telegramOptions.BotDisplayName);
            var welcomeText = $"👋 Ласкаво просимо до <b>{safeBotName}</b>!\n\n" +
                              "📌 <b>Як користуватися:</b>\n" +
                              "1️⃣ Надішліть посилання на продукт з АТБ, Сільпо, Maudau або METRO\n" +
                              "2️⃣ Бот автоматично відстежуватиме зміни його ціни\n" +
                              "3️⃣ Використовуйте меню нижче для керування вашими підписками\n\n" +
                              $"📦 Ви можете мати до <code>{_telegramOptions.MaxSubscriptionsPerUser}</code> активних підписок одночасно.";

            await _telegram.SendMessageWithKeyboardAsync(
                chatId,
                welcomeText,
                GetMainKeyboard(),
                cancellationToken);
        }

        private static string BuildLocationPromptBody() =>
            "📍 <b>Вкажіть вашу локацію</b>, щоб ми могли підбирати найближчі магазини.\n\n" +
            "• Напишіть назву населеного пункту або адресу текстом\n" +
            "• Або натисніть <b>📍 Поділитися геолокацією</b>";

        private static IReplyMarkup GetLocationKeyboard()
        {
            return new ReplyKeyboardMarkup(
                new[]
                {
                    new[] { KeyboardButton.WithRequestLocation(ShareLocationButtonText) },
                    new[] { new KeyboardButton(BackButtonText) }
                })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }

        private static InlineKeyboardMarkup BuildConfirmKeyboard(IReadOnlyList<GeocodingResult> results)
        {
            var rows = new List<InlineKeyboardButton[]>();
            for (var i = 0; i < results.Count; i++)
            {
                var label = TruncateButtonLabel(results[i].DisplayName);
                rows.Add(
                [
                    InlineKeyboardButton.WithCallbackData(label, $"{CallbackPickPrefix}{i}")
                ]);
            }

            rows.Add(
            [
                InlineKeyboardButton.WithCallbackData(BackButtonText, CallbackBack)
            ]);

            return new InlineKeyboardMarkup(rows);
        }

        private static string TruncateButtonLabel(string displayName)
        {
            const int maxLen = 64;
            if (displayName.Length <= maxLen)
            {
                return displayName;
            }

            return displayName[..(maxLen - 1)] + "…";
        }

        internal static IReplyMarkup GetMainKeyboard()
        {
            return new ReplyKeyboardMarkup(
                new[]
                {
                    new[] { new KeyboardButton("📋 Мої підписки") },
                    new[] { new KeyboardButton("❓ Інструкції") }
                })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };
        }
    }
}
