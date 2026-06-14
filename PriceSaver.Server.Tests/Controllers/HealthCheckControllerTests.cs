using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Controllers;

namespace PriceSaver.Server.Tests.Controllers
{
    public class HealthCheckControllerTests
    {
        [Fact]
        public void Get_ReturnsOk()
        {
            var controller = new HealthCheckController();

            var result = controller.Get();

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().Be("PriceSaver service is healthy!");
        }
    }
}
