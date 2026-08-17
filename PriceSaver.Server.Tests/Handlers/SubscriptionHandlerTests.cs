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

        private static Subscription Sub(string name, string url, StoreType store, decimal price, Guid? id = null) =>
            new()
            {
                Id = id ?? Guid.NewGuid(),
                UserId = ChatId,
                ProductUrl = url,
                ProductName = name,
                StoreType = store,
                CurrentPrice = price,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

        private static bool HasCallback(IReplyMarkup? markup, string callbackData) =>
            markup is InlineKeyboardMarkup inline &&
            inline.InlineKeyboard.SelectMany(r => r).Any(b => b.CallbackData == callbackData);

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
            telegram.Verify(t => t.SendRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReplyMarkup?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task SendSubscriptionsAsync_SendsSingleRichList_WithSelectButtons()
        {
            var sub1 = Sub("A", "https://atb/1", StoreType.ATB, 10m);
            var sub2 = Sub("B", "https://silpo/2", StoreType.Silpo, 20m);

            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.GetActiveSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subscription> { sub1, sub2 });
            var telegram = new Mock<ITelegramService>();
            telegram
                .Setup(t => t.SendRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReplyMarkup?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.SendSubscriptionsAsync(ChatId, CancellationToken.None);

            telegram.Verify(t => t.SendRichMessageAsync(
                    ChatId,
                    It.Is<string>(s =>
                        s.Contains("Мої підписки") &&
                        s.Contains("| # | Товар | Магазин | Ціна | ↑ |") &&
                        s.Contains("A") &&
                        s.Contains("B")),
                    It.Is<IReplyMarkup>(m =>
                        HasCallback(m, $"sub_sel_0_{sub1.Id}") &&
                        HasCallback(m, $"sub_sel_0_{sub2.Id}")),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            telegram.Verify(t => t.SendMessageWithKeyboardAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReplyMarkup>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task SendSubscriptionsAsync_SendsUpgradeMessage_WhenRichSendFails()
        {
            var sub1 = Sub("A", "https://atb/1", StoreType.ATB, 10m);
            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.GetActiveSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subscription> { sub1 });
            var telegram = new Mock<ITelegramService>();
            telegram
                .Setup(t => t.SendRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReplyMarkup?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.SendSubscriptionsAsync(ChatId, CancellationToken.None);

            telegram.Verify(t => t.SendMessageAsync(
                    ChatId,
                    It.Is<string>(s => s.Contains("оновіть Telegram")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleSelectSubscriptionCallbackAsync_EditsToDetail()
        {
            var sub = Sub("Coffee", "https://atb/1", StoreType.ATB, 12.5m);
            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.GetActiveSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subscription> { sub });
            var telegram = new Mock<ITelegramService>();
            telegram
                .Setup(t => t.EditRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleSelectSubscriptionCallbackAsync(ChatId, "cbq", 0, sub.Id.ToString(), 42, CancellationToken.None);

            telegram.Verify(t => t.EditRichMessageAsync(
                    ChatId,
                    42,
                    It.Is<string>(s => s.Contains("Coffee") && s.Contains("Магазин")),
                    It.Is<InlineKeyboardMarkup>(m =>
                        m.InlineKeyboard.SelectMany(r => r).Any(b => b.CallbackData == $"sub_remove_0_{sub.Id}") &&
                        m.InlineKeyboard.SelectMany(r => r).Any(b => b.CallbackData == $"sub_list_0")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            telegram.Verify(t => t.AnswerCallbackQueryAsync("cbq", null, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleListPageCallbackAsync_EditsToRequestedPage()
        {
            var subs = Enumerable.Range(1, 12)
                .Select(i => Sub($"P{i}", $"https://atb/{i}", StoreType.ATB, i))
                .ToList();

            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.GetActiveSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(subs);
            var telegram = new Mock<ITelegramService>();
            telegram
                .Setup(t => t.EditRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleListPageCallbackAsync(ChatId, "cbq", 1, 77, CancellationToken.None);

            telegram.Verify(t => t.EditRichMessageAsync(
                    ChatId,
                    77,
                    It.Is<string>(s => s.Contains("11–12 з 12") && s.Contains("P11")),
                    It.Is<InlineKeyboardMarkup>(m =>
                        m.InlineKeyboard.SelectMany(r => r).Any(b => b.CallbackData == $"sub_list_0")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleRemoveSubscriptionCallbackAsync_ReturnsError_WhenSubscriptionIdInvalid()
        {
            var subscriptionService = new Mock<ISubscriptionService>();
            var telegram = new Mock<ITelegramService>();

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleRemoveSubscriptionCallbackAsync(ChatId, "cbq", 0, "not-a-guid", 1, CancellationToken.None);

            telegram.Verify(t => t.AnswerCallbackQueryAsync(
                    "cbq", It.Is<string>(s => s.Contains("Некоректний")), true, It.IsAny<CancellationToken>()),
                Times.Once);
            subscriptionService.Verify(s => s.DeactivateSubscriptionAsync(
                    It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleRemoveSubscriptionCallbackAsync_RemovesAndReturnsToList_WhenValid()
        {
            var subscriptionId = Guid.NewGuid();
            var remaining = Sub("Left", "https://atb/2", StoreType.ATB, 5m);
            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.DeactivateSubscriptionAsync(ChatId, subscriptionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeactivateSubscriptionResult(DeactivateSubscriptionStatus.Success));
            subscriptionService
                .Setup(s => s.GetActiveSubscriptionsAsync(ChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subscription> { remaining });
            var telegram = new Mock<ITelegramService>();
            telegram
                .Setup(t => t.EditRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleRemoveSubscriptionCallbackAsync(ChatId, "cbq", 0, subscriptionId.ToString(), 99, CancellationToken.None);

            telegram.Verify(t => t.DeleteMessageAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            telegram.Verify(t => t.EditRichMessageAsync(
                    ChatId,
                    99,
                    It.Is<string>(s => s.Contains("Left") && s.Contains("Мої підписки")),
                    It.IsAny<InlineKeyboardMarkup?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
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

            await sut.HandleRemoveSubscriptionCallbackAsync(ChatId, "cbq", 0, subscriptionId.ToString(), 99, CancellationToken.None);

            telegram.Verify(t => t.AnswerCallbackQueryAsync(
                    "cbq", It.Is<string>(s => s.Contains("раніше")), true, It.IsAny<CancellationToken>()),
                Times.Once);
            telegram.Verify(t => t.EditRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleToggleNotifyOnIncreaseCallbackAsync_UpdatesDetailInPlace()
        {
            var sub = Sub("Coffee", "https://atb/1", StoreType.ATB, 12.5m);
            sub.NotifyOnIncrease = false;
            var toggled = Sub("Coffee", "https://atb/1", StoreType.ATB, 12.5m, sub.Id);
            toggled.NotifyOnIncrease = true;

            var subscriptionService = new Mock<ISubscriptionService>();
            subscriptionService
                .Setup(s => s.ToggleNotifyOnIncreaseAsync(ChatId, sub.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToggleNotifyOnIncreaseResult(ToggleNotifyOnIncreaseStatus.Success, toggled));
            var telegram = new Mock<ITelegramService>();
            telegram
                .Setup(t => t.EditRichMessageAsync(
                    It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<InlineKeyboardMarkup?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = CreateHandler(subscriptionService, telegram);

            await sut.HandleToggleNotifyOnIncreaseCallbackAsync(ChatId, "cbq", 0, sub.Id.ToString(), 55, CancellationToken.None);

            telegram.Verify(t => t.EditRichMessageAsync(
                    ChatId,
                    55,
                    It.Is<string>(s => s.Contains("увімкнено")),
                    It.Is<InlineKeyboardMarkup>(m =>
                        m.InlineKeyboard.SelectMany(r => r).Any(b => b.CallbackData == $"sub_toggle_increase_0_{sub.Id}")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            telegram.Verify(t => t.AnswerCallbackQueryAsync(
                    "cbq", It.Is<string>(s => s.Contains("увімкнено")), false, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        public static IEnumerable<object[]> CreateStatusCases()
        {
            var sub = new Subscription { Id = Guid.NewGuid(), ProductUrl = "https://atb/x", ProductName = "Widget", StoreType = StoreType.ATB, CurrentPrice = 12.5m };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.Created, sub), "Підписку створено" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.Reactivated, sub), "Підписку створено" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.AlreadyActive, sub), "вже існує" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.UnsupportedStore), "не підтримується" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.LimitReached), "ліміту" };
            yield return new object[] { new CreateSubscriptionResult(CreateSubscriptionStatus.ParseFailed), "Не вдалося отримати" };
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

        [Theory]
        [InlineData("0_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", 0, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", true)]
        [InlineData("2_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", 2, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", true)]
        [InlineData("not-paged", 0, "", false)]
        public void TryParsePagedSubscriptionCallback_ParsesExpected(string payload, int expectedPage, string expectedId, bool expectedOk)
        {
            var ok = SubscriptionHandler.TryParsePagedSubscriptionCallback(payload, out var page, out var id);

            ok.Should().Be(expectedOk);
            if (expectedOk)
            {
                page.Should().Be(expectedPage);
                id.Should().Be(expectedId);
            }
        }
    }
}
