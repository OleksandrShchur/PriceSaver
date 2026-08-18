using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private const string DefaultLogPath = "logs/pricesaver-.txt";
        private readonly IConfiguration _configuration;
        private readonly ITelegramAlertService _alertService;
        private readonly ILogger<LogsController> _logger;
        private readonly TimeSpan _backlogSendDelay;

        public LogsController(
            IConfiguration configuration,
            ITelegramAlertService alertService,
            ILogger<LogsController> logger,
            TimeSpan? backlogSendDelay = null)
        {
            _configuration = configuration;
            _alertService = alertService;
            _logger = logger;
            _backlogSendDelay = backlogSendDelay ?? TimeSpan.FromSeconds(1);
        }

        [HttpPost("yesterday")]
        public async Task<IActionResult> GetYesterdayLogs()
        {
            if (!IsAuthorized())
            {
                return Unauthorized();
            }

            var yesterday = DateTime.Today.AddDays(-1);
            var fullPath = GetLogFilePath(yesterday);
            var fileName = Path.GetFileName(fullPath);

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { error = "Log file for yesterday not found" });
            }

            var caption = $"📋 PriceSaver Logs — {yesterday:dd.MM.yyyy}";
            var sent = await _alertService.SendLogFileAsync(fullPath, caption);
            if (!sent)
            {
                _logger.LogError("Failed to send yesterday's log file {FileName} to Telegram channel", fileName);
                return StatusCode(500, new { error = "Failed to send log file to Telegram channel" });
            }

            TryDeleteSentLogFile(fullPath);

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

        [HttpPost("backlog")]
        public async Task<IActionResult> SendBacklogLogs()
        {
            if (!IsAuthorized())
            {
                return Unauthorized();
            }

            var files = GetRolledLogFiles();
            if (files.Count == 0)
            {
                return NotFound(new { error = "No rolled log files found" });
            }

            var sent = new List<string>();
            var failed = new List<string>();

            for (var i = 0; i < files.Count; i++)
            {
                var (path, date) = files[i];
                var fileName = Path.GetFileName(path);
                var caption = $"📋 PriceSaver Logs — {date:dd.MM.yyyy}";
                var ok = await _alertService.SendLogFileAsync(path, caption);
                if (ok)
                {
                    sent.Add(fileName);
                    TryDeleteSentLogFile(path);
                }
                else
                {
                    failed.Add(fileName);
                }

                if (i < files.Count - 1 && _backlogSendDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_backlogSendDelay);
                }
            }

            if (failed.Count > 0)
            {
                _logger.LogError(
                    "Failed to send {FailedCount} of {TotalCount} backlog log files to Telegram channel",
                    failed.Count,
                    files.Count);
                return StatusCode(500, new
                {
                    error = "Failed to send one or more log files to Telegram channel",
                    sent,
                    failed
                });
            }

            var ip = HttpContext.Connection.RemoteIpAddress;
            _logger.LogInformation(
                "Sent {Count} backlog log files to Telegram channel by request from {IP}",
                sent.Count,
                ip);

            return Ok(new
            {
                message = "Log files sent to Telegram channel",
                sent
            });
        }

        private void TryDeleteSentLogFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete sent log file {FilePath}", path);
            }
        }

        private bool IsAuthorized()
        {
            var secretKey = _configuration["LogRetrieval:SecretKey"];
            var key = Request.Headers["X-Api-Key"].ToString();
            return !string.IsNullOrEmpty(secretKey) && key == secretKey;
        }

        private string GetLogPathPattern() =>
            _configuration["Logging:FilePath"] ?? DefaultLogPath;

        private string GetLogFilePath(DateTime date)
        {
            var logPath = GetLogPathPattern();
            var directory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrEmpty(directory))
            {
                directory = "logs";
            }

            return Path.Combine(directory, GetLogFileName(date));
        }

        private string GetLogFileName(DateTime date)
        {
            var logPath = GetLogPathPattern();
            var fileNamePattern = Path.GetFileName(logPath);
            var baseName = Path.GetFileNameWithoutExtension(fileNamePattern);
            var extension = Path.GetExtension(fileNamePattern);
            return $"{baseName}{date:yyyyMMdd}{extension}";
        }

        private IReadOnlyList<(string Path, DateTime Date)> GetRolledLogFiles()
        {
            var logPath = GetLogPathPattern();
            var directory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrEmpty(directory))
            {
                directory = "logs";
            }

            if (!Directory.Exists(directory))
            {
                return [];
            }

            var fileNamePattern = Path.GetFileName(logPath);
            var baseName = Path.GetFileNameWithoutExtension(fileNamePattern);
            var extension = Path.GetExtension(fileNamePattern);
            var todayName = GetLogFileName(DateTime.Today);

            return Directory.GetFiles(directory, $"{baseName}????????{extension}")
                .Select(path => (Path: path, FileName: Path.GetFileName(path)))
                .Where(f => !f.FileName.Equals(todayName, StringComparison.OrdinalIgnoreCase))
                .Select(f => (f.Path, Date: TryParseLogDate(f.FileName, baseName, extension)))
                .Where(f => f.Date.HasValue)
                .OrderBy(f => f.Date!.Value)
                .Select(f => (f.Path, f.Date!.Value))
                .ToList();
        }

        private static DateTime? TryParseLogDate(string fileName, string baseName, string extension)
        {
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (!withoutExtension.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var datePart = withoutExtension[baseName.Length..];
            if (DateTime.TryParseExact(
                    datePart,
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                return date;
            }

            return null;
        }
    }
}
