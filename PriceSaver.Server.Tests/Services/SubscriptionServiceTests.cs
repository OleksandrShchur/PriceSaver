using Microsoft.Extensions.Options;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Services
{
    public class SubscriptionServiceTests
    {
        private const long UserId = 42;
        private const string Url = "https://www.atbmarket.com/product/123";

        private static SubscriptionService CreateService(
            ApplicationDbContext db,
            IPriceParser[] parsers,
            out Mock<IUserService> userService,
            int maxSubscriptions = 50)
        {
            userService = new Mock<IUserService>();
            userService
                .Setup(s => s.EnsureUserExistsAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { MaxSubscriptionsPerUser = maxSubscriptions });
            var logger = new TestLogger<SubscriptionService>();

            return new SubscriptionService(db, userService.Object, options, logger, parsers);
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsAlreadyActive_WhenActiveSubscriptionExists()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Subscriptions.Add(new Subscription
            {
                UserId = UserId,
                ProductUrl = Url,
                IsActive = true,
                ProductName = "Existing",
                CurrentPrice = 50m
            });
            await db.SaveChangesAsync();

            var sut = CreateService(db, [new FakePriceParser()], out _);

            var result = await sut.CreateSubscriptionAsync(UserId, "user", Url, CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.AlreadyActive);
            result.Subscription.Should().NotBeNull();
            result.Subscription!.ProductName.Should().Be("Existing");
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsUnsupportedStore_WhenNoParserMatches()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var parser = new FakePriceParser(canParse: _ => false);

            var sut = CreateService(db, [parser], out _);

            var result = await sut.CreateSubscriptionAsync(UserId, "user", "https://unknown.example/p/1", CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.UnsupportedStore);
            result.Subscription.Should().BeNull();
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsLimitReached_WhenUserHasMaxSubscriptions()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Subscriptions.Add(new Subscription
            {
                UserId = UserId,
                ProductUrl = "https://www.atbmarket.com/product/other",
                IsActive = true
            });
            await db.SaveChangesAsync();

            var sut = CreateService(db, [new FakePriceParser()], out _, maxSubscriptions: 1);

            var result = await sut.CreateSubscriptionAsync(UserId, "user", Url, CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.LimitReached);
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsCreated_AndPersistsSubscription()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var parser = new FakePriceParser(
                parse: (_, _) => Task.FromResult(("Milk 1L", 32.50m)));

            var sut = CreateService(db, [parser], out var userService);

            var result = await sut.CreateSubscriptionAsync(UserId, "user", Url, CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.Created);
            result.Subscription!.ProductName.Should().Be("Milk 1L");
            result.Subscription.CurrentPrice.Should().Be(32.50m);
            result.Subscription.IsActive.Should().BeTrue();
            result.Subscription.LastCheckedDate.Should().NotBeNull();

            db.Subscriptions.Should().ContainSingle(s => s.UserId == UserId && s.ProductUrl == Url && s.IsActive);
            userService.Verify(s => s.EnsureUserExistsAsync(UserId, "user", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsReactivated_WhenInactiveSubscriptionReused()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var inactiveId = Guid.NewGuid();
            db.Subscriptions.Add(new Subscription
            {
                Id = inactiveId,
                UserId = UserId,
                ProductUrl = Url,
                StoreType = StoreType.Unknown,
                IsActive = false,
                ProductName = "Old name",
                CurrentPrice = 10m,
                NotifyOnIncrease = true
            });
            await db.SaveChangesAsync();

            var parser = new FakePriceParser(
                storeKey: "atb",
                parse: (_, _) => Task.FromResult(("Refreshed", 99m)));
            var sut = CreateService(db, [parser], out _);

            var result = await sut.CreateSubscriptionAsync(UserId, "user", Url, CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.Reactivated);
            result.Subscription!.Id.Should().Be(inactiveId);
            result.Subscription.IsActive.Should().BeTrue();
            result.Subscription.StoreType.Should().Be(StoreType.ATB);
            result.Subscription.ProductName.Should().Be("Refreshed");
            result.Subscription.CurrentPrice.Should().Be(99m);
            result.Subscription.NotifyOnIncrease.Should().BeTrue();
            result.Subscription.LastCheckedDate.Should().NotBeNull();

            db.Subscriptions.Should().ContainSingle();
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsReactivated_WhenUrlDiffersOnlyByTrailingSlash()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var inactiveId = Guid.NewGuid();
            db.Subscriptions.Add(new Subscription
            {
                Id = inactiveId,
                UserId = UserId,
                ProductUrl = "https://www.atbmarket.com/product/123",
                IsActive = false,
                ProductName = "Old name",
                CurrentPrice = 10m
            });
            await db.SaveChangesAsync();

            var parser = new FakePriceParser(
                parse: (_, _) => Task.FromResult(("Refreshed", 99m)));
            var sut = CreateService(db, [parser], out _);

            var result = await sut.CreateSubscriptionAsync(
                UserId,
                "user",
                "https://www.atbmarket.com/product/123/",
                CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.Reactivated);
            result.Subscription!.Id.Should().Be(inactiveId);
            db.Subscriptions.Should().ContainSingle();
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsParseFailed_WhenParserThrows()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var parser = new FakePriceParser(
                parse: (_, _) => throw new InvalidOperationException("boom"));
            var sut = CreateService(db, [parser], out _);

            var result = await sut.CreateSubscriptionAsync(UserId, "user", Url, CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.ParseFailed);
            result.Subscription.Should().BeNull();
            db.Subscriptions.Should().BeEmpty();
        }

        [Theory]
        [InlineData("atb", StoreType.ATB)]
        [InlineData("silpo", StoreType.Silpo)]
        [InlineData("maudau", StoreType.Maudau)]
        [InlineData("unknownstore", StoreType.Unknown)]
        public async Task CreateSubscriptionAsync_InfersStoreType_FromParserStoreKey(string storeKey, StoreType expected)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var parser = new FakePriceParser(
                storeKey: storeKey,
                parse: (_, _) => Task.FromResult(("Product", 1m)));
            var sut = CreateService(db, [parser], out _);

            var result = await sut.CreateSubscriptionAsync(UserId, "user", Url, CancellationToken.None);

            result.Status.Should().Be(CreateSubscriptionStatus.Created);
            result.Subscription!.StoreType.Should().Be(expected);
        }

        [Fact]
        public async Task GetActiveSubscriptionsAsync_ReturnsOnlyActive_OrderedByCreatedAt()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var older = new Subscription { UserId = UserId, ProductUrl = "a", IsActive = true, CreatedAt = DateTime.UtcNow.AddMinutes(-10) };
            var newer = new Subscription { UserId = UserId, ProductUrl = "b", IsActive = true, CreatedAt = DateTime.UtcNow };
            db.Subscriptions.AddRange(
                newer,
                older,
                new Subscription { UserId = UserId, ProductUrl = "c", IsActive = false });
            await db.SaveChangesAsync();

            var sut = CreateService(db, [new FakePriceParser()], out _);

            var result = await sut.GetActiveSubscriptionsAsync(UserId, CancellationToken.None);

            result.Should().HaveCount(2);
            result.Select(s => s.ProductUrl).Should().ContainInOrder("a", "b");
        }

        [Fact]
        public async Task DeactivateSubscriptionAsync_ReturnsSuccess_AndDeactivates()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var id = Guid.NewGuid();
            db.Subscriptions.Add(new Subscription { Id = id, UserId = UserId, ProductUrl = "a", IsActive = true });
            await db.SaveChangesAsync();

            var sut = CreateService(db, [new FakePriceParser()], out _);

            var result = await sut.DeactivateSubscriptionAsync(UserId, id, CancellationToken.None);

            result.Status.Should().Be(DeactivateSubscriptionStatus.Success);
            db.Subscriptions.Single(s => s.Id == id).IsActive.Should().BeFalse();
        }

        [Fact]
        public async Task DeactivateSubscriptionAsync_ReturnsNotFound_WhenMissing()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var sut = CreateService(db, [new FakePriceParser()], out _);

            var result = await sut.DeactivateSubscriptionAsync(UserId, Guid.NewGuid(), CancellationToken.None);

            result.Status.Should().Be(DeactivateSubscriptionStatus.NotFound);
        }
    }
}
