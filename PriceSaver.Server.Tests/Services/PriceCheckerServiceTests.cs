using Microsoft.Extensions.Logging;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Services
{
    public class PriceCheckerServiceTests
    {
        private const long UserId = 7;
        private const string Url = "https://www.atbmarket.com/product/abc";

        private static Subscription SeedSubscription(
            ApplicationDbContext db,
            decimal currentPrice,
            bool notifyOnIncrease = false)
        {
            var sub = new Subscription
            {
                UserId = UserId,
                ProductUrl = Url,
                StoreType = StoreType.ATB,
                ProductName = "Tracked product",
                CurrentPrice = currentPrice,
                IsActive = true,
                NotifyOnIncrease = notifyOnIncrease
            };
            db.Subscriptions.Add(sub);
            db.SaveChanges();
            return sub;
        }

        [Fact]
        public async Task CheckAllAsync_LogsWarning_AndContinues_WhenNoParserMatches()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            SeedSubscription(db, 100m);

            var parser = new FakePriceParser(canParse: _ => false);
            var telegram = new Mock<ITelegramService>();
            var logger = new TestLogger<PriceCheckerService>();
            var sut = new PriceCheckerService(db, [parser], telegram.Object, logger);

            await sut.CheckAllAsync(CancellationToken.None);

            logger.HasLevel(LogLevel.Warning).Should().BeTrue();
            db.PriceHistories.Should().BeEmpty();
            telegram.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CheckAllAsync_OnPriceDrop_WritesHistory_AndSendsDownwardNotification()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var sub = SeedSubscription(db, 100m);

            var parser = new FakePriceParser(parse: (_, _) => Task.FromResult(("Tracked product", 80m)));
            var telegram = new Mock<ITelegramService>();
            var logger = new TestLogger<PriceCheckerService>();
            var sut = new PriceCheckerService(db, [parser], telegram.Object, logger);

            await sut.CheckAllAsync(CancellationToken.None);

            db.Subscriptions.Single(s => s.Id == sub.Id).CurrentPrice.Should().Be(80m);
            db.PriceHistories.Should().ContainSingle(p => p.SubscriptionId == sub.Id && p.Price == 80m);
            telegram.Verify(t => t.SendMessageAsync(
                    UserId,
                    It.Is<string>(s => s.Contains("знизилася")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CheckAllAsync_OnPriceIncrease_WithNotifyOnIncrease_SendsUpwardNotification()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var sub = SeedSubscription(db, 100m, notifyOnIncrease: true);

            var parser = new FakePriceParser(parse: (_, _) => Task.FromResult(("Tracked product", 120m)));
            var telegram = new Mock<ITelegramService>();
            var logger = new TestLogger<PriceCheckerService>();
            var sut = new PriceCheckerService(db, [parser], telegram.Object, logger);

            await sut.CheckAllAsync(CancellationToken.None);

            db.PriceHistories.Should().ContainSingle(p => p.SubscriptionId == sub.Id && p.Price == 120m);
            telegram.Verify(t => t.SendMessageAsync(
                    UserId,
                    It.Is<string>(s => s.Contains("зросла")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CheckAllAsync_OnPriceIncrease_WithoutNotifyOnIncrease_DoesNotNotify()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var sub = SeedSubscription(db, 100m, notifyOnIncrease: false);

            var parser = new FakePriceParser(parse: (_, _) => Task.FromResult(("Tracked product", 120m)));
            var telegram = new Mock<ITelegramService>();
            var logger = new TestLogger<PriceCheckerService>();
            var sut = new PriceCheckerService(db, [parser], telegram.Object, logger);

            await sut.CheckAllAsync(CancellationToken.None);

            // Price changed, so history is still recorded ...
            db.PriceHistories.Should().ContainSingle(p => p.SubscriptionId == sub.Id);
            // ... but no message is sent for an unwanted increase.
            telegram.Verify(t => t.SendMessageAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CheckAllAsync_OnUnchangedPrice_DoesNotWriteHistoryOrNotify()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            SeedSubscription(db, 100m);

            var parser = new FakePriceParser(parse: (_, _) => Task.FromResult(("Tracked product", 100m)));
            var telegram = new Mock<ITelegramService>();
            var logger = new TestLogger<PriceCheckerService>();
            var sut = new PriceCheckerService(db, [parser], telegram.Object, logger);

            await sut.CheckAllAsync(CancellationToken.None);

            db.PriceHistories.Should().BeEmpty();
            telegram.Verify(t => t.SendMessageAsync(
                    It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CheckAllAsync_OnParserException_LogsError_AndContinues()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            SeedSubscription(db, 100m);

            var parser = new FakePriceParser(parse: (_, _) => throw new HttpRequestException("network down"));
            var telegram = new Mock<ITelegramService>();
            var logger = new TestLogger<PriceCheckerService>();
            var sut = new PriceCheckerService(db, [parser], telegram.Object, logger);

            var act = async () => await sut.CheckAllAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();
            logger.HasLevel(LogLevel.Error).Should().BeTrue();
            db.PriceHistories.Should().BeEmpty();
        }
    }
}
