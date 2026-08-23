using System.Text.Json;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramUser = Telegram.Bot.Types.User;

namespace PriceSaver.Server.Tests.Handlers
{
    public class LocationOnboardingHandlerTests
    {
        private const long ChatId = 42;

        private static LocationOnboardingHandler CreateSut(
            Mock<ITelegramService> telegram,
            Mock<IUserService> userService,
            Mock<INominatimGeocodingService> geocoding)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions
            {
                BotDisplayName = "PriceSaver",
                MaxSubscriptionsPerUser = 50
            });

            return new LocationOnboardingHandler(
                telegram.Object,
                userService.Object,
                geocoding.Object,
                options,
                new TestLogger<LocationOnboardingHandler>());
        }

        [Fact]
        public async Task HandleTypedLocationAsync_NoMatch_RePrompts()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var geocoding = new Mock<INominatimGeocodingService>();
            geocoding.Setup(g => g.SearchAsync("nowhere", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<GeocodingResult>());

            var sut = CreateSut(telegram, userService, geocoding);

            await sut.HandleTypedLocationAsync(ChatId, "nowhere", CancellationToken.None);

            userService.Verify(u => u.SetConversationStateAsync(
                ChatId, ConversationStates.AwaitingLocation, null, It.IsAny<CancellationToken>()), Times.Once);
            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("не знайдено")),
                    It.IsAny<IReplyMarkup>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleTypedLocationAsync_SingleMatch_SavesImmediately()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var geocoding = new Mock<INominatimGeocodingService>();
            geocoding.Setup(g => g.SearchAsync("Kyiv", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[]
                {
                    new GeocodingResult
                    {
                        DisplayName = "Kyiv, Ukraine",
                        Latitude = 50.45m,
                        Longitude = 30.52m
                    }
                });

            var sut = CreateSut(telegram, userService, geocoding);

            await sut.HandleTypedLocationAsync(ChatId, "Kyiv", CancellationToken.None);

            userService.Verify(u => u.SaveLocationAsync(
                ChatId, 50.45m, 30.52m, "Kyiv, Ukraine", It.IsAny<CancellationToken>()), Times.Once);
            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("збережено")),
                    It.IsAny<IReplyMarkup>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleTypedLocationAsync_MultipleMatches_StoresConfirmState()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var geocoding = new Mock<INominatimGeocodingService>();
            var matches = new[]
            {
                new GeocodingResult { DisplayName = "A", Latitude = 1m, Longitude = 2m },
                new GeocodingResult { DisplayName = "B", Latitude = 3m, Longitude = 4m }
            };
            geocoding.Setup(g => g.SearchAsync("Ambiguous", It.IsAny<CancellationToken>()))
                .ReturnsAsync(matches);

            var sut = CreateSut(telegram, userService, geocoding);

            await sut.HandleTypedLocationAsync(ChatId, "Ambiguous", CancellationToken.None);

            userService.Verify(u => u.SetConversationStateAsync(
                ChatId,
                ConversationStates.AwaitingLocationConfirm,
                It.Is<string>(p => p.Contains("displayName")),
                It.IsAny<CancellationToken>()), Times.Once);
            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("кілька варіантів")),
                    It.IsAny<IReplyMarkup>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleCallbackAsync_Pick_SavesChosenCandidate()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var geocoding = new Mock<INominatimGeocodingService>();

            var payload = JsonSerializer.Serialize(
                new[]
                {
                    new GeocodingResult { DisplayName = "First", Latitude = 1m, Longitude = 2m },
                    new GeocodingResult { DisplayName = "Second", Latitude = 3m, Longitude = 4m }
                },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            userService.Setup(u => u.GetAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.User
                {
                    TelegramId = ChatId,
                    ConversationState = ConversationStates.AwaitingLocationConfirm,
                    ConversationPayload = payload
                });

            var sut = CreateSut(telegram, userService, geocoding);

            var callback = new CallbackQuery
            {
                Id = "cb-1",
                Data = LocationOnboardingHandler.CallbackPickPrefix + "1",
                From = new TelegramUser { Id = ChatId, FirstName = "T" }
            };

            await sut.HandleCallbackAsync(callback, CancellationToken.None);

            userService.Verify(u => u.SaveLocationAsync(
                ChatId, 3m, 4m, "Second", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleCallbackAsync_Back_RePrompts()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var geocoding = new Mock<INominatimGeocodingService>();
            var sut = CreateSut(telegram, userService, geocoding);

            var callback = new CallbackQuery
            {
                Id = "cb-back",
                Data = LocationOnboardingHandler.CallbackBack,
                From = new TelegramUser { Id = ChatId, FirstName = "T" }
            };

            await sut.HandleCallbackAsync(callback, CancellationToken.None);

            userService.Verify(u => u.SetConversationStateAsync(
                ChatId, ConversationStates.AwaitingLocation, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleSharedLocationAsync_UsesReverseName_OrFallback()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var geocoding = new Mock<INominatimGeocodingService>();
            geocoding.Setup(g => g.ReverseAsync(50.1m, 30.2m, It.IsAny<CancellationToken>()))
                .ReturnsAsync((GeocodingResult?)null);

            var sut = CreateSut(telegram, userService, geocoding);

            await sut.HandleSharedLocationAsync(
                ChatId,
                new Location { Latitude = 50.1, Longitude = 30.2 },
                CancellationToken.None);

            userService.Verify(u => u.SaveLocationAsync(
                ChatId,
                It.Is<decimal>(lat => Math.Abs(lat - 50.1m) < 0.0001m),
                It.Is<decimal>(lon => Math.Abs(lon - 30.2m) < 0.0001m),
                LocationOnboardingHandler.SharedLocationFallbackName,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
