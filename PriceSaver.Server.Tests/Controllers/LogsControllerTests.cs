using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PriceSaver.Server.Controllers;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Controllers
{
    public class LogsControllerTests : IDisposable
    {
        private const string Secret = "log-secret";
        private readonly string _tempDir;

        public LogsControllerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"ps-logs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Fact]
        public async Task GetYesterdayLogs_ReturnsUnauthorized_WhenApiKeyInvalid()
        {
            var controller = CreateController("wrong-key", out _);

            var result = await controller.GetYesterdayLogs();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task GetYesterdayLogs_ReturnsUnauthorized_WhenApiKeyMissing()
        {
            var controller = CreateController(null, out _);

            var result = await controller.GetYesterdayLogs();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task GetYesterdayLogs_ReturnsNotFound_WhenYesterdayFileMissing()
        {
            var controller = CreateController(Secret, out _);

            var result = await controller.GetYesterdayLogs();

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task GetYesterdayLogs_ReturnsOk_WhenSendSucceeds()
        {
            var yesterday = DateTime.Today.AddDays(-1);
            var filePath = CreateLogFile(yesterday, "yesterday");
            var controller = CreateController(Secret, out var alerts);

            var result = await controller.GetYesterdayLogs();

            result.Should().BeOfType<OkObjectResult>();
            alerts.SentLogFiles.Should().ContainSingle()
                .Which.FilePath.Should().Be(filePath);
        }

        [Fact]
        public async Task GetYesterdayLogs_Returns500_WhenSendFails()
        {
            CreateLogFile(DateTime.Today.AddDays(-1), "yesterday");
            var controller = CreateController(Secret, out var alerts);
            alerts.SendLogFileResult = false;

            var result = await controller.GetYesterdayLogs();

            result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task SendBacklogLogs_ReturnsUnauthorized_WhenApiKeyInvalid()
        {
            var controller = CreateController("wrong-key", out _);

            var result = await controller.SendBacklogLogs();

            result.Should().BeOfType<UnauthorizedResult>();
        }

        [Fact]
        public async Task SendBacklogLogs_ReturnsNotFound_WhenOnlyTodayExists()
        {
            CreateLogFile(DateTime.Today, "today");
            var controller = CreateController(Secret, out var alerts);

            var result = await controller.SendBacklogLogs();

            result.Should().BeOfType<NotFoundObjectResult>();
            alerts.SentLogFiles.Should().BeEmpty();
        }

        [Fact]
        public async Task SendBacklogLogs_ReturnsOk_SendingRolledFilesOldestFirst()
        {
            var older = DateTime.Today.AddDays(-2);
            var yesterday = DateTime.Today.AddDays(-1);
            var olderPath = CreateLogFile(older, "older");
            var yesterdayPath = CreateLogFile(yesterday, "yesterday");
            CreateLogFile(DateTime.Today, "today");
            var controller = CreateController(Secret, out var alerts);

            var result = await controller.SendBacklogLogs();

            result.Should().BeOfType<OkObjectResult>();
            alerts.SentLogFiles.Select(s => s.FilePath).Should().Equal(olderPath, yesterdayPath);
            alerts.SentLogFiles[0].Caption.Should().Contain(older.ToString("dd.MM.yyyy"));
            alerts.SentLogFiles[1].Caption.Should().Contain(yesterday.ToString("dd.MM.yyyy"));
        }

        [Fact]
        public async Task SendBacklogLogs_Returns500_WhenAnySendFails()
        {
            CreateLogFile(DateTime.Today.AddDays(-2), "older");
            CreateLogFile(DateTime.Today.AddDays(-1), "yesterday");
            var controller = CreateController(Secret, out var alerts);
            alerts.SendLogFileResult = false;

            var result = await controller.SendBacklogLogs();

            result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(500);
        }

        private string CreateLogFile(DateTime date, string contents)
        {
            var fileName = $"pricesaver-{date:yyyyMMdd}.txt";
            var path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private LogsController CreateController(string? apiKeyHeader, out RecordingTelegramAlertService alerts)
        {
            alerts = new RecordingTelegramAlertService();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LogRetrieval:SecretKey"] = Secret,
                    ["Logging:FilePath"] = Path.Combine(_tempDir, "pricesaver-.txt"),
                })
                .Build();

            var controller = new LogsController(
                configuration,
                alerts,
                new TestLogger<LogsController>(),
                TimeSpan.Zero)
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
    }
}
