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
        private readonly IMaudauScraperService _maudauScraper;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<JobsController> _logger;
        private readonly JobsOptions _options;

        public JobsController(
            PriceCheckerService checker,
            IMaudauScraperService maudauScraper,
            IServiceScopeFactory scopeFactory,
            ILogger<JobsController> logger,
            IOptions<JobsOptions> options)
        {
            _checker = checker;
            _maudauScraper = maudauScraper;
            _scopeFactory = scopeFactory;
            _logger = logger;
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

        /// <summary>
        /// Triggers the daily Maudau market import. The actual scraping runs as a
        /// background job so the HTTP request returns immediately.
        /// </summary>
        [HttpPost("scrape-maudau")]
        public IActionResult ScrapeMaudau()
        {
            var key = Request.Headers["X-Api-Key"].ToString();
            if (string.IsNullOrEmpty(_options.SecretKey) || key != _options.SecretKey)
            {
                return Unauthorized();
            }

            // Fire-and-forget background job with its own DI scope so it outlives the request.
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var scraper = scope.ServiceProvider.GetRequiredService<IMaudauScraperService>();

                try
                {
                    await scraper.ScrapeAllAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Maudau scrape background job failed.");
                }
            });

            return Accepted(new { status = "started" });
        }
    }
}
