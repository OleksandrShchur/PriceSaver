using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Integration
{
    public class JobsEndpointTests : IClassFixture<PriceSaverWebApplicationFactory>
    {
        private readonly PriceSaverWebApplicationFactory _factory;

        public JobsEndpointTests(PriceSaverWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task CheckPrices_Returns401_WhenApiKeyInvalid()
        {
            var client = _factory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/check-prices");
            request.Headers.Add("X-Api-Key", "nope");

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task CheckPrices_RunsCheck_WritesHistory_AndNotifies_WhenAuthorized()
        {
            // Arrange: a subscription whose price will drop from 200 -> 100 (fake parser returns 100).
            var subId = Guid.NewGuid();
            const long userId = 1234;
            _factory.SeedDb(db =>
            {
                db.Subscriptions.Add(new Subscription
                {
                    Id = subId,
                    UserId = userId,
                    ProductUrl = "https://example.com/product/1",
                    StoreType = StoreType.ATB,
                    ProductName = "Seeded product",
                    CurrentPrice = 200m,
                    IsActive = true
                });
            });

            var client = _factory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/check-prices");
            request.Headers.Add("X-Api-Key", PriceSaverWebApplicationFactory.JobsSecret);

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.Subscriptions.Single(s => s.Id == subId).CurrentPrice.Should().Be(100m);
            db.PriceHistories.Should().Contain(p => p.SubscriptionId == subId && p.Price == 100m);

            _factory.Telegram.Messages.Should().Contain(m => m.ChatId == userId && m.Text.Contains("знизилася"));
        }
    }
}
