using Microsoft.Extensions.Options;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Tests.Handlers
{
    public class SubscriptionHandlerTests
    {
        private const long ChatId = 555;

        private static SubscriptionHandler CreateHandler(
            Mock<ISubscriptionService> subscriptionService,
            Mock<ITelegramService> telegram,
            int maxSubscriptions = 50)
        {
            var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { MaxSubscriptionsPerUser = maxSubscriptions });
            var logger = new TestLogger<SubscriptionHandler>();
            return new SubscriptionHandler(subscriptionService.Object, telegram.Object, options, logger);
        }

        [Fact]
        public async Task SendSubscriptionsAsync_SendsNoActiveMessage_WhenListEmpty()
        {
            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.GetActiveSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subscription>());
            var telegram = new Mock<ITelegramService>();

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.SendSubscriptionsAsync(ChatId, CancellationToken.None);

            telegram.Verify(t => t.SendMessageAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("немає активних підписок")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            telegram.Verify(t => t.SendMessageWithInlineButtonAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task SendSubscriptionsAsync_SendsInlineDeleteButtons_ForEachSubscription()
        {
            var sub1 = new Subscription { Id = Guid.NewGuid(), UserId = ChatId, ProductUrl = "https://atb/1", ProductName = "A", StoreType = StoreType.ATB, CurrentPrice = 10m };
            var sub2 = new Subscription { Id = Guid.NewGuid(), UserId = ChatId, ProductUrl = "https://silpo/2", ProductName = "B", StoreType = StoreType.Silpo, CurrentPrice = 20m };

            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.GetActiveSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subscription> { sub1, sub2 });
            var telegram = new Mock<ITelegramService>();

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.SendSubscriptionsAsync(ChatId, CancellationToken.None);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("A")),
                    It.Is<IReplyMarkup>(m =>
                        m is InlineKeyboardMarkup &&
                        ((InlineKeyboardMarkup)m).InlineKeyboard.Any(row =>
                            row.Any(b => b.Text == "🗑️ Видалити" && b.CallbackData == $"sub_remove_{sub1.Id}"))),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("B")),
                    It.Is<IReplyMarkup>(m =>
                        m is InlineKeyboardMarkup &&
                        ((InlineKeyboardMarkup)m).InlineKeyboard.Any(row =>
                            row.Any(b => b.Text == "🗑️ Видалити" && b.CallbackData == $"sub_remove_{sub2.Id}"))),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleRemoveSubscriptionCallbackAsync_ReturnsError_WhenSubscriptionIdInvalid()
        {
            var subscriptionService = new Mock<ISubscriptionService>();
            var telegram = new Mock<ITelegramService>();

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleRemoveSubscriptionCallbackAsync(ChatId, "cbq", "not-a-guid", 1, CancellationToken.None);

            telegram.Verify(t => t.AnswerCallbackQueryAsync(
                    "cbq", It.Is<string>(s => s.Contains("Некоректний")), true, It.IsAny<CancellationToken>()),
                Times.Once);
            subscriptionService.Verify(s => s.DeactivateSubscriptionAsync(
                    It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleRemoveSubscriptionCallbackAsync_RemovesAndConfirms_WhenValid()
        {
            var subscriptionId = Guid.NewGuid();
            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.DeactivateSubscriptionAsync(ChatId, subscriptionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeactivateSubscriptionResult(DeactivateSubscriptionStatus.Success));
            var telegram = new Mock<ITelegramService>();

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleRemoveSubscriptionCallbackAsync(ChatId, "cbq", subscriptionId.ToString(), 99, CancellationToken.None);

            telegram.Verify(t => t.DeleteMessageAsync(ChatId, 99, It.IsAny<CancellationToken>()), Times.Once);
            telegram.Verify(t => t.AnswerCallbackQueryAsync(
                    "cbq", It.Is<string>(s => s.Contains("видалено")), false, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleRemoveSubscriptionCallbackAsync_AnswersAlreadyRemoved_WhenNotFound()
        {
            var subscriptionId = Guid.NewGuid();
            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.DeactivateSubscriptionAsync(ChatId, subscriptionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeactivateSubscriptionResult(DeactivateSubscriptionStatus.NotFound));
            var telegram = new Mock<ITelegramService>();

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleRemoveSubscriptionCallbackAsync(ChatId, "cbq", subscriptionId.ToString(), 99, CancellationToken.None);

            telegram.Verify(t => t.AnswerCallbackQueryAsync(
                    "cbq", It.Is<string>(s => s.Contains("раніше")), true, It.IsAny<CancellationToken>()),
                Times.Once);
            telegram.Verify(t => t.DeleteMessageAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public static IEnumerable<object[]> CreateStatusCases()
        {
            var sub = new Subscription { Id = Guid.NewGuid(), ProductUrl = "https://atb/x", ProductName = "Widget", StoreType = StoreType.ATB, CurrentPrice = 12.5m };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.Created, sub), "Підписку створено" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.Reactivated, sub), "Підписку створено" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.AlreadyActive, sub), "вже існує" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.UnsupportedStore), "не підтримується" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.LimitReached), "ліміту" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.ParseFailed), "Неможливо отримати" };
        }

        [Theory]
        [MemberData(nameof(CreateStatusCases))]
        public async Task CreateSubscriptionAsync_FormatsMessage_ForEachStatus(CreateSubscriptionResult result, string expectedFragment)
        {
            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.CreateSubscriptionAsync(ChatId, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            var telegram = new Mock<ITelegramService>();

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.CreateSubscriptionAsync(ChatId, "user", "https://atb/x", CancellationToken.None);

            telegram.Verify(t => t.SendMessageAsync(
                    ChatId, It.Is<string>(s => s.Contains(expectedFragment)), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
