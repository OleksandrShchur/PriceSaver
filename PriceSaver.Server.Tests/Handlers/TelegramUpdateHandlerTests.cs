using Microsoft.Extensions.Options;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramUser = Telegram.Bot.Types.User;

namespace PriceSaver.Server.Tests.Handlers
{
    public class TelegramUpdateHandlerTests
    {
        private const long ChatId = 9001;

        private static TelegramUpdateHandler CreateHandler(
            Mock<ITelegramService> telegram,
            Mock<IUserService> userService,
            Mock<ISubscriptionHandler> subscriptionHandler,
            Mock<ILocationOnboardingHandler>? locationOnboarding = null)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { BotDisplayName = "PriceSaver", MaxSubscriptionsPerUser = 50 });
            var logger = new TestLogger<TelegramUpdateHandler>();
            locationOnboarding ??= new Mock<ILocationOnboardingHandler>();
            return new TelegramUpdateHandler(
                telegram.Object,
                options,
                userService.Object,
                subscriptionHandler.Object,
                locationOnboarding.Object,
                logger);
        }

        private static void SetupUserWithLocation(Mock<IUserService> userService, bool hasLocation = true)
        {
            userService.Setup(u => u.HasLocationAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasLocation);

            userService.Setup(u => u.GetAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.User
                {
                    TelegramId = ChatId,
                    Username = "user",
                    Latitude = hasLocation ? 50m : null,
                    Longitude = hasLocation ? 30m : null,
                    LocationName = hasLocation ? "Kyiv" : null,
                    ConversationState = ConversationStates.None
                });
        }

        private static Update TextUpdate(string text, string? username = "user") => new()
        {
            Message = new Message
            {
                Text = text,
                Chat = new Chat { Id = ChatId, Type = ChatType.Private },
                From = new TelegramUser { Id = ChatId, Username = username, FirstName = "Test" }
            }
        };

        [Fact]
        public async Task HandleAsync_OnStart_WithoutLocation_PromptsForLocation()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: false);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("/start"), CancellationToken.None);

            userService.Verify(u => u.EnsureUserExistsAsync(ChatId, "user", It.IsAny<CancellationToken>()), Times.Once);
            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId, It.Is<string>(s => s.Contains("Ласкаво просимо")), It.IsAny<IReplyMarkup>(), It.IsAny<CancellationToken>()),
                Times.Once);
            location.Verify(l => l.PromptForLocationAsync(ChatId, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnStart_WithLocation_DoesNotPrompt()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("/start"), CancellationToken.None);

            location.Verify(l => l.PromptForLocationAsync(
                It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_OnMySubscriptions_WithoutLocation_RedirectsToLocationFlow()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: false);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("/my_subscriptions"), CancellationToken.None);

            subscriptionHandler.Verify(s => s.SendSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()), Times.Never);
            location.Verify(l => l.PromptForLocationAsync(ChatId, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnMySubscriptions_WithLocation_ForwardsToSubscriptionListing()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("/my_subscriptions"), CancellationToken.None);

            subscriptionHandler.Verify(s => s.SendSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnInstructions_WithoutLocation_StillSendsInstructions()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: false);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("/instructions"), CancellationToken.None);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId, It.Is<string>(s => s.Contains("Підтримувані магазини")), It.IsAny<IReplyMarkup>(), It.IsAny<CancellationToken>()),
                Times.Once);
            location.Verify(l => l.PromptForLocationAsync(
                It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task HandleAsync_OnUrl_WithoutLocation_RedirectsToLocationFlow()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: false);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("https://www.atbmarket.com/product/42"), CancellationToken.None);

            subscriptionHandler.Verify(s => s.CreateSubscriptionAsync(
                    It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            location.Verify(l => l.PromptForLocationAsync(ChatId, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnUrl_WithLocation_TriggersCreateSubscriptionFlow()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            const string url = "https://www.atbmarket.com/product/42";
            await sut.HandleAsync(TextUpdate(url), CancellationToken.None);

            telegram.Verify(t => t.SendMessageAsync(
                    ChatId, It.Is<string>(s => s.Contains("Перевіряємо")), It.IsAny<CancellationToken>()),
                Times.Once);
            subscriptionHandler.Verify(s => s.CreateSubscriptionAsync(
                    ChatId, "user", url, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnUnsupportedText_WithLocation_SendsMainKeyboardPrompt()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            SetupUserWithLocation(userService, hasLocation: true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("just some chatter"), CancellationToken.None);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId, It.Is<string>(s => s.Contains("Надішліть пряме посилання")), It.IsAny<IReplyMarkup>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnAwaitingLocationText_ForwardsToLocationHandler()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();

            userService.Setup(u => u.HasLocationAsync(ChatId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            userService.Setup(u => u.GetAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Models.User
                {
                    TelegramId = ChatId,
                    ConversationState = ConversationStates.AwaitingLocation
                });

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            await sut.HandleAsync(TextUpdate("Lviv"), CancellationToken.None);

            location.Verify(l => l.HandleTypedLocationAsync(ChatId, "Lviv", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnLocationMessage_ForwardsToSharedLocationHandler()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            var update = new Update
            {
                Message = new Message
                {
                    Chat = new Chat { Id = ChatId, Type = ChatType.Private },
                    From = new TelegramUser { Id = ChatId, Username = "user", FirstName = "Test" },
                    Location = new Location { Latitude = 50.45, Longitude = 30.52 }
                }
            };

            await sut.HandleAsync(update, CancellationToken.None);

            location.Verify(l => l.HandleSharedLocationAsync(
                    ChatId,
                    It.Is<Location>(loc => loc.Latitude == 50.45 && loc.Longitude == 30.52),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnSubCallback_WithoutLocation_Redirects()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            location.Setup(l => l.IsLocationCallback(It.IsAny<string?>())).Returns(false);
            SetupUserWithLocation(userService, hasLocation: false);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            var subscriptionId = Guid.NewGuid();
            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "cbq-1",
                    Data = $"sub_remove_0_{subscriptionId}",
                    From = new TelegramUser { Id = ChatId, FirstName = "Test" },
                    Message = new Message
                    {
                        MessageId = 321,
                        Chat = new Chat { Id = ChatId, Type = ChatType.Private }
                    }
                }
            };

            await sut.HandleAsync(update, CancellationToken.None);

            subscriptionHandler.Verify(s => s.HandleRemoveSubscriptionCallbackAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
            location.Verify(l => l.PromptForLocationAsync(ChatId, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnCallbackQuery_RoutesToRemovalHandler_WhenHasLocation()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            location.Setup(l => l.IsLocationCallback(It.IsAny<string?>())).Returns(false);
            SetupUserWithLocation(userService, hasLocation: true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            var subscriptionId = Guid.NewGuid();
            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "cbq-1",
                    Data = $"sub_remove_0_{subscriptionId}",
                    From = new TelegramUser { Id = ChatId, FirstName = "Test" },
                    Message = new Message
                    {
                        MessageId = 321,
                        Chat = new Chat { Id = ChatId, Type = ChatType.Private }
                    }
                }
            };

            await sut.HandleAsync(update, CancellationToken.None);

            subscriptionHandler.Verify(s => s.HandleRemoveSubscriptionCallbackAsync(
                    ChatId, "cbq-1", 0, subscriptionId.ToString(), 321, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnCallbackQuery_RoutesToSelectHandler_WhenHasLocation()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            location.Setup(l => l.IsLocationCallback(It.IsAny<string?>())).Returns(false);
            SetupUserWithLocation(userService, hasLocation: true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            var subscriptionId = Guid.NewGuid();
            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "cbq-sel",
                    Data = $"sub_sel_1_{subscriptionId}",
                    From = new TelegramUser { Id = ChatId, FirstName = "Test" },
                    Message = new Message
                    {
                        MessageId = 10,
                        Chat = new Chat { Id = ChatId, Type = ChatType.Private }
                    }
                }
            };

            await sut.HandleAsync(update, CancellationToken.None);

            subscriptionHandler.Verify(s => s.HandleSelectSubscriptionCallbackAsync(
                    ChatId, "cbq-sel", 1, subscriptionId.ToString(), 10, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnCallbackQuery_RoutesToListPageHandler_WhenHasLocation()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            location.Setup(l => l.IsLocationCallback(It.IsAny<string?>())).Returns(false);
            SetupUserWithLocation(userService, hasLocation: true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "cbq-list",
                    Data = "sub_list_2",
                    From = new TelegramUser { Id = ChatId, FirstName = "Test" },
                    Message = new Message
                    {
                        MessageId = 11,
                        Chat = new Chat { Id = ChatId, Type = ChatType.Private }
                    }
                }
            };

            await sut.HandleAsync(update, CancellationToken.None);

            subscriptionHandler.Verify(s => s.HandleListPageCallbackAsync(
                    ChatId, "cbq-list", 2, 11, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnLocationCallback_RoutesToLocationHandler()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();
            var location = new Mock<ILocationOnboardingHandler>();
            location.Setup(l => l.IsLocationCallback("loc_back")).Returns(true);

            var sut = CreateHandler(telegram, userService, subscriptionHandler, location);

            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "cbq-loc",
                    Data = "loc_back",
                    From = new TelegramUser { Id = ChatId, FirstName = "Test" },
                    Message = new Message
                    {
                        MessageId = 11,
                        Chat = new Chat { Id = ChatId, Type = ChatType.Private }
                    }
                }
            };

            await sut.HandleAsync(update, CancellationToken.None);

            location.Verify(l => l.HandleCallbackAsync(It.IsAny<CallbackQuery>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
