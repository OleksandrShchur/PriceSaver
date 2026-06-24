using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ITelegramAlertService _alertService;
        private readonly ILogger<LogsController> _logger;

        public LogsController(
            IConfiguration configuration,
            ITelegramAlertService alertService,
            ILogger<LogsController> logger)
        {
            _configuration = configuration;
            _alertService = alertService;
            _logger = logger;
        }

        [HttpPost("yesterday")]
        public async Task<IActionResult> GetYesterdayLogs()
        {
            var secretKey = _configuration["LogRetrieval:SecretKey"];
            var key = Request.Headers["X-Api-Key"].ToString();
            if (string.IsNullOrEmpty(secretKey) || key != secretKey)
            {
                return Unauthorized();
            }

            var yesterday = DateTime.UtcNow.AddDays(-1);
            var dateSuffix = yesterday.ToString("yyyyMMdd");
            var logPath = _configuration["Logging:FilePath"] ?? "logs/pricesaver-.txt";
            var directory = Path.GetDirectoryName(logPath) ?? "logs";
            var fileNamePattern = Path.GetFileName(logPath);
            var baseName = Path.GetFileNameWithoutExtension(fileNamePattern);
            var extension = Path.GetExtension(fileNamePattern);
            var fileName = $"{baseName}{dateSuffix}{extension}";
            var fullPath = Path.Combine(directory, fileName);

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = "Log file for yesterday not found" });
            }

            var caption = $"📋 PriceSaver Logs — {yesterday:dd.MM.yyyy}";
            await _alertService.SendLogFileAsync(fullPath, caption);

            var ip = HttpContext.Connection.RemoteIpAddress;
            _logger.LogInformation(
                "Yesterday's log file {FileName} successfully sent to Telegram channel by request from {IP}",
                fileName,
                ip);

            return Ok(new
            {
                message = "Log file sent to Telegram channel",
                date = yesterday.ToString("dd.MM.yyyy")
            });
        }
    }
}
