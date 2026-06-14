using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Integration
{
    public class TelegramEndpointTests : IClassFixture<PriceSaverWebApplicationFactory>
    {
        private readonly PriceSaverWebApplicationFactory _factory;

        public TelegramEndpointTests(PriceSaverWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private static StringContent Json(string body) =>
            new(body, Encoding.UTF8, "application/json");

        [Fact]
        public async Task Post_ReturnsBadRequest_OnEmptyBody()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsync("/api/telegram", Json("   "));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Post_DeserializesUpdate_AndDelegatesToHandler()
        {
            var recording = new RecordingUpdateHandler();
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITelegramUpdateHandler>();
                    services.AddScoped<ITelegramUpdateHandler>(_ => recording);
                });
            }).CreateClient();

            const string body = """
            { "update_id": 555, "message": { "message_id": 1, "date": 0, "chat": { "id": 88, "type": "private" }, "text": "hello" } }
            """;

            var response = await client.PostAsync("/api/telegram", Json(body));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            recording.Handled.Should().ContainSingle();
            recording.Handled.TryPeek(out var update).Should().BeTrue();
            update!.Id.Should().Be(555);
        }
    }
}
