using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Integration
{
    /// <summary>
    /// Exercises the full subscription lifecycle through the real Telegram
    /// webhook pipeline (controller → update handler → subscription handler →
    /// subscription service → EF Core in-memory database).
    /// </summary>
    public class SubscriptionLifecycleTests : IClassFixture<PriceSaverWebApplicationFactory>
    {
        private const long ChatId = 7777;
        private readonly PriceSaverWebApplicationFactory _factory;

        public SubscriptionLifecycleTests(PriceSaverWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        private static string MessageUpdate(int updateId, int messageId, string text) => $$"""
        {
          "update_id": {{updateId}},
          "message": {
            "message_id": {{messageId}},
            "date": 0,
            "chat": { "id": {{ChatId}}, "type": "private" },
            "from": { "id": {{ChatId}}, "is_bot": false, "first_name": "Test", "username": "tester" },
            "text": "{{text}}"
          }
        }
        """;

        private static string CallbackUpdate(int updateId, int messageId, string data) => $$"""
        {
          "update_id": {{updateId}},
          "callback_query": {
            "id": "cbq-1",
            "chat_instance": "1",
            "from": { "id": {{ChatId}}, "is_bot": false, "first_name": "Test" },
            "message": {
              "message_id": {{messageId}},
              "date": 0,
              "chat": { "id": {{ChatId}}, "type": "private" }
            },
            "data": "{{data}}"
          }
        }
        """;

        [Fact]
        public async Task FullLifecycle_Create_List_Remove()
        {
            var client = _factory.CreateClient();

            // 1. Create a subscription by sending a product URL.
            var createResponse = await client.PostAsync(
                "/api/telegram",
                Json(MessageUpdate(1, 10, "https://example.com/product/9")));
            createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            Guid subscriptionId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sub = db.Subscriptions.Single(s => s.UserId == ChatId && s.IsActive);
                sub.ProductName.Should().Be("Integration Product");
                sub.CurrentPrice.Should().Be(100m);
                sub.StoreType.Should().Be(StoreType.ATB);
                db.Users.Should().Contain(u => u.TelegramId == ChatId);
                subscriptionId = sub.Id;
            }

            _factory.Telegram.Messages.Should().Contain(m => m.Text.Contains("Підписку створено"));

            // 2. List subscriptions -> an inline delete button is sent.
            var listResponse = await client.PostAsync(
                "/api/telegram",
                Json(MessageUpdate(2, 11, "/my_subscriptions")));
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            _factory.Telegram.InlineButtons.Should()
                .Contain(b => b.CallbackData == $"sub_remove_{subscriptionId}");

            // 3. Remove the subscription via the inline button callback.
            var removeResponse = await client.PostAsync(
                "/api/telegram",
                Json(CallbackUpdate(3, 12, $"sub_remove_{subscriptionId}")));
            removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Subscriptions.Single(s => s.Id == subscriptionId).IsActive.Should().BeFalse();
            }

            _factory.Telegram.CallbackAnswers.Should().Contain(a => a.Text!.Contains("видалено"));

            // 4. Re-add the same product -> reactivate the existing row instead of inserting a new one.
            var recreateResponse = await client.PostAsync(
                "/api/telegram",
                Json(MessageUpdate(4, 13, "https://example.com/product/9")));
            recreateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Subscriptions.Should().ContainSingle();
                var sub = db.Subscriptions.Single();
                sub.Id.Should().Be(subscriptionId);
                sub.IsActive.Should().BeTrue();
                sub.ProductName.Should().Be("Integration Product");
                sub.CurrentPrice.Should().Be(100m);
            }

            _factory.Telegram.Messages.Should().Contain(m => m.Text.Contains("Підписку створено"));
        }
    }
}
