using Microsoft.AspNetCore.Mvc;

namespace PriceSaver.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthCheckController : ControllerBase
    {
        [HttpGet]
        [HttpHead]
        public IActionResult Get() => Ok("PriceSaver service is healthy!");
    }
}
