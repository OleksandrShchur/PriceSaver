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
            while (_factory.Telegram.Messages.TryDequeue(out _)) { }
            while (_factory.Telegram.RichMessages.TryDequeue(out _)) { }
            while (_factory.Telegram.InlineButtons.TryDequeue(out _)) { }
            while (_factory.Telegram.EditedRichMessages.TryDequeue(out _)) { }
            while (_factory.Telegram.CallbackAnswers.TryDequeue(out _)) { }

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

            // 2. List subscriptions -> one rich table with a select button for the item.
            var listResponse = await client.PostAsync(
                "/api/telegram",
                Json(MessageUpdate(2, 11, "/my_subscriptions")));
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            _factory.Telegram.RichMessages.Should().ContainSingle(m => m.ChatId == ChatId);
            _factory.Telegram.InlineButtons.Should()
                .Contain(b => b.CallbackData == $"sub_sel_0_{subscriptionId}");

            // 3. Open detail, then remove the subscription via the detail delete button.
            var selectResponse = await client.PostAsync(
                "/api/telegram",
                Json(CallbackUpdate(3, 12, $"sub_sel_0_{subscriptionId}")));
            selectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            _factory.Telegram.EditedRichMessages.Should()
                .Contain(m => m.MessageId == 12 && m.Markdown.Contains("Integration Product"));

            var removeResponse = await client.PostAsync(
                "/api/telegram",
                Json(CallbackUpdate(4, 12, $"sub_remove_0_{subscriptionId}")));
            removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Subscriptions.Single(s => s.Id == subscriptionId).IsActive.Should().BeFalse();
            }

            _factory.Telegram.CallbackAnswers.Should().Contain(a => a.Text != null && a.Text.Contains("видалено"));

            // 4. Re-add the same product -> reactivate the existing row instead of inserting a new one.
            var recreateResponse = await client.PostAsync(
                "/api/telegram",
                Json(MessageUpdate(5, 13, "https://example.com/product/9")));
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
