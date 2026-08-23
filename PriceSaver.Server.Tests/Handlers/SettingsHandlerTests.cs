using PriceSaver.Server.Handlers;
using PriceSaver.Server.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramUser = Telegram.Bot.Types.User;
using User = PriceSaver.Server.Models.User;

namespace PriceSaver.Server.Tests.Handlers
{
    public class SettingsHandlerTests
    {
        private const long ChatId = 77;

        [Fact]
        public async Task SendSettingsAsync_ShowsLocationNameAndChangeButton()
        {
            var telegram = new Mock<ITelegramService>();
            var location = new Mock<ILocationOnboardingHandler>();
            var sut = new SettingsHandler(telegram.Object, location.Object);

            var user = new User
            {
                TelegramId = ChatId,
                Latitude = 50.45m,
                Longitude = 30.52m,
                LocationName = "Kyiv, Ukraine"
            };

            await sut.SendSettingsAsync(ChatId, user, CancellationToken.None);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("Налаштування") && s.Contains("Kyiv, Ukraine")),
                    It.Is<IReplyMarkup>(m => MarkupHasChangeLocation(m)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SendSettingsAsync_FallsBackToCoordinates_WhenNameMissing()
        {
            var telegram = new Mock<ITelegramService>();
            var location = new Mock<ILocationOnboardingHandler>();
            var sut = new SettingsHandler(telegram.Object, location.Object);

            var user = new User
            {
                TelegramId = ChatId,
                Latitude = 50.45m,
                Longitude = 30.52m
            };

            await sut.SendSettingsAsync(ChatId, user, CancellationToken.None);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("50.45") && s.Contains("30.52")),
                    It.IsAny<IReplyMarkup>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleCallbackAsync_ChangeLocation_PromptsForLocation()
        {
            var telegram = new Mock<ITelegramService>();
            var location = new Mock<ILocationOnboardingHandler>();
            var sut = new SettingsHandler(telegram.Object, location.Object);

            var callback = new CallbackQuery
            {
                Id = "cbq-1",
                Data = SettingsHandler.CallbackChangeLocation,
                From = new TelegramUser { Id = ChatId, FirstName = "Test" }
            };

            await sut.HandleCallbackAsync(callback, CancellationToken.None);

            telegram.Verify(t => t.AnswerCallbackQueryAsync(
                    "cbq-1", null, false, It.IsAny<CancellationToken>()),
                Times.Once);
            location.Verify(l => l.PromptForLocationAsync(ChatId, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void GetMainKeyboard_IncludesSettingsButton()
        {
            var markup = LocationOnboardingHandler.GetMainKeyboard();
            markup.Should().BeOfType<ReplyKeyboardMarkup>();

            var keyboard = (ReplyKeyboardMarkup)markup;
            var labels = keyboard.Keyboard.SelectMany(row => row).Select(b => b.Text).ToList();

            labels.Should().Contain(SettingsHandler.SettingsButtonText);
            labels.Should().Contain("📋 Мої підписки");
            labels.Should().Contain("❓ Інструкції");
        }

        private static bool MarkupHasChangeLocation(IReplyMarkup markup)
        {
            if (markup is not InlineKeyboardMarkup inline)
            {
                return false;
            }

            return inline.InlineKeyboard
                .SelectMany(row => row)
                .Any(b => b.CallbackData == SettingsHandler.CallbackChangeLocation);
        }
    }
}
