using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Controllers
{
    [ApiController]
    [Route("api/jobs")]
    public class JobsController : ControllerBase
    {
        private readonly PriceCheckerService _checker;
        private readonly JobsOptions _options;

        public JobsController(PriceCheckerService checker, IOptions<JobsOptions> options)
        {
            _checker = checker;
            _options = options.Value;
        }

        [HttpPost("check-prices")]
        public async Task<IActionResult> CheckPrices()
        {
            var key = Request.Headers["X-Api-Key"].ToString();
            if (string.IsNullOrEmpty(_options.SecretKey) || key != _options.SecretKey)
            {
                return Unauthorized();
            }

            await _checker.CheckAllAsync();
            return Ok(new { status = "started" });
        }
    }
}
