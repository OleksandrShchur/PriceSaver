using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Controllers;
using PriceSaver.Server.Handlers;
using Telegram.Bot.Types;

namespace PriceSaver.Server.Tests.Controllers
{
    public class TelegramControllerTests
    {
        private static TelegramController CreateController(string body, out Mock<ITelegramUpdateHandler> handler)
        {
            handler = new Mock<ITelegramUpdateHandler>();
            var controller = new TelegramController(handler.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
            controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return controller;
        }

        [Fact]
        public async Task Post_ReturnsBadRequest_WhenPayloadDeserializesToNull()
        {
            var controller = CreateController("   ", out var handler);

            var result = await controller.Post(CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            handler.Verify(h => h.HandleAsync(It.IsAny<Update>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Post_ReturnsOk_AndDelegates_WhenPayloadValid()
        {
            const string body = """
            { "update_id": 100, "message": { "message_id": 1, "date": 0, "chat": { "id": 5, "type": "private" }, "text": "hi" } }
            """;
            var controller = CreateController(body, out var handler);

            var result = await controller.Post(CancellationToken.None);

            result.Should().BeOfType<OkResult>();
            handler.Verify(h => h.HandleAsync(
                    It.Is<Update>(u => u.Id == 100), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public void Get_ReturnsOk()
        {
            var controller = new TelegramController(Mock.Of<ITelegramUpdateHandler>());

            var result = controller.Get();

            result.Should().BeOfType<OkObjectResult>();
        }
    }
}
