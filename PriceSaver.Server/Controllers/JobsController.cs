using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Controllers
{
    [ApiController]
    [Route("api/jobs")]
    public class JobsController : ControllerBase
    {
        private readonly PriceCheckerService _checker;
        private readonly ILogger<JobsController> _logger;
        private readonly IConfiguration _config;

        public JobsController(PriceCheckerService checker, ILogger<JobsController> logger, IConfiguration config)
        {
            _checker = checker;
            _logger = logger;
            _config = config;
        }

        [HttpPost("check-prices")]
        public async Task<IActionResult> CheckPrices()
        {
            var key = Request.Headers["X-Api-Key"].ToString();
            var secret = _config["Jobs:SecretKey"];
            if (string.IsNullOrEmpty(secret) || key != secret)
            {
                return Unauthorized();
            }

            await _checker.CheckAllAsync();
            return Ok(new { status = "started" });
        }
    }
}
