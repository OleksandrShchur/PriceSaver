using Microsoft.Extensions.Options;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Tests.Handlers
{
    public class TelegramUpdateHandlerTests
    {
        private const long ChatId = 9001;

        private static TelegramUpdateHandler CreateHandler(
            Mock<ITelegramService> telegram,
            Mock<IUserService> userService,
            Mock<ISubscriptionHandler> subscriptionHandler)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { BotDisplayName = "PriceSaver", MaxSubscriptionsPerUser = 50 });
            var logger = new TestLogger<TelegramUpdateHandler>();
            return new TelegramUpdateHandler(telegram.Object, options, userService.Object, subscriptionHandler.Object, logger);
        }

        private static Update TextUpdate(string text, string? username = "user") => new()
        {
            Message = new Message
            {
                Text = text,
                Chat = new Chat { Id = ChatId, Type = ChatType.Private },
                From = new User { Id = ChatId, Username = username, FirstName = "Test" }
            }
        };

        [Fact]
        public async Task HandleAsync_OnStart_SendsWelcome_AndEnsuresUser()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();

            var sut = CreateHandler(telegram, userService, subscriptionHandler);

            await sut.HandleAsync(TextUpdate("/start"), CancellationToken.None);

            userService.Verify(u => u.EnsureUserExistsAsync(ChatId, "user", It.IsAny<CancellationToken>()), Times.Once);
            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId, It.Is<string>(s => s.Contains("Ласкаво просимо")), It.IsAny<IReplyMarkup>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnMySubscriptions_ForwardsToSubscriptionListing()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();

            var sut = CreateHandler(telegram, userService, subscriptionHandler);

            await sut.HandleAsync(TextUpdate("/my_subscriptions"), CancellationToken.None);

            subscriptionHandler.Verify(s => s.SendSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnInstructions_SendsInstructions()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();

            var sut = CreateHandler(telegram, userService, subscriptionHandler);

            await sut.HandleAsync(TextUpdate("/instructions"), CancellationToken.None);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId, It.Is<string>(s => s.Contains("Підтримувані магазини")), It.IsAny<IReplyMarkup>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAsync_OnUrl_TriggersCreateSubscriptionFlow()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();

            var sut = CreateHandler(telegram, userService, subscriptionHandler);

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
        public async Task HandleAsync_OnUnsupportedText_SendsMainKeyboardPrompt()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();

            var sut = CreateHandler(telegram, userService, subscriptionHandler);

            await sut.HandleAsync(TextUpdate("just some chatter"), CancellationToken.None);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId, It.Is<string>(s => s.Contains("Надішліть пряме посилання")), It.IsAny<IReplyMarkup>(), It.IsAny<CancellationToken>()),
                Times.Once);
            subscriptionHandler.Verify(s => s.CreateSubscriptionAsync(
                    It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleAsync_OnCallbackQuery_RoutesToRemovalHandler()
        {
            var telegram = new Mock<ITelegramService>();
            var userService = new Mock<IUserService>();
            var subscriptionHandler = new Mock<ISubscriptionHandler>();

            var sut = CreateHandler(telegram, userService, subscriptionHandler);

            var subscriptionId = Guid.NewGuid();
            var update = new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "cbq-1",
                    Data = $"sub_remove_{subscriptionId}",
                    From = new User { Id = ChatId, FirstName = "Test" },
                    Message = new Message
                    {
                        MessageId = 321,
                        Chat = new Chat { Id = ChatId, Type = ChatType.Private }
                    }
                }
            };

            await sut.HandleAsync(update, CancellationToken.None);

            subscriptionHandler.Verify(s => s.HandleRemoveSubscriptionCallbackAsync(
                    ChatId, "cbq-1", subscriptionId.ToString(), 321, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
