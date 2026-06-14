using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Controllers;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Controllers
{
    public class JobsControllerTests
    {
        private const string Secret = "top-secret";

        private static JobsController CreateController(string? apiKeyHeader, out RecordingTelegramService telegram)
        {
            var db = TestDbContextFactory.CreateInMemory();
            telegram = new RecordingTelegramService();
            var logger = new TestLogger<PriceCheckerService>();
            var checker = new PriceCheckerService(db, [new FakePriceParser()], telegram, logger);
            var options = Microsoft.Extensions.Options.Options.Create(new JobsOptions { SecretKey = Secret });

            var controller = new JobsController(checker, options)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            if (apiKeyHeader is not null)
            {
                controller.HttpContext.Request.Headers["X-Api-Key"] = apiKeyHeader;
            }

            return controller;
        }

        [Fact]
        public async Task CheckPrices_ReturnsOk_WhenApiKeyValid()
        {
            var controller = CreateController(Secret, out _);

            var result = await controller.CheckPrices();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task CheckPrices_ReturnsUnauthorized_WhenApiKeyInvalid()
        {
            var controller = CreateController("wrong-key", out _);

            var result = await controller.CheckPrices();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task CheckPrices_ReturnsUnauthorized_WhenApiKeyMissing()
        {
            var controller = CreateController(null, out _);

            var result = await controller.CheckPrices();

            result.Should().BeOfType<UnauthorizedResult>();
        }
    }
}
