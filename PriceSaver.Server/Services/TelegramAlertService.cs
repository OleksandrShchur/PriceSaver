using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;

namespace PriceSaver.Server.Services
{
    public class TelegramAlertService : ITelegramAlertService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelegramAlertService> _logger;
        private readonly TelegramOptions _options;
        private readonly Lazy<TelegramBotClient?> _client;

        public TelegramAlertService(
            IOptions<TelegramOptions> options,
            IConfiguration configuration,
            ILogger<TelegramAlertService> logger)
        {
            _options = options.Value;
            _configuration = configuration;
            _logger = logger;
            _client = new Lazy<TelegramBotClient?>(CreateClient);
        }

        public async Task SendErrorAlertAsync(string message, Exception? exception = null)
        {
            var client = _client.Value;
            var channelId = _configuration["TelegramAlerts:ChannelId"];
            if (client is null || string.IsNullOrWhiteSpace(channelId))
            {
                _logger.LogError("Telegram alert skipped: TelegramAlerts:ChannelId is not configured");
                return;
            }

            try
            {
                var text =
                    "🚨 *PriceSaver Error Alert*\n" +
                    $"🕐 `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`\n" +
                    $"📌 *Message:* `{EscapeMarkdown(message)}`\n" +
                    $"🔍 *Exception:* `{exception?.GetType().Name ?? "N/A"}`\n" +
                    $"📄 *Details:* `{EscapeMarkdown(exception?.Message ?? "–")}`";

                await client.SendTextMessageAsync(
                    chatId: channelId,
                    text: text,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Telegram error alert");
            }
        }

        public async Task SendLogFileAsync(string filePath, string caption)
        {
            var client = _client.Value;
            var channelId = _configuration["TelegramAlerts:ChannelId"];
            if (client is null || string.IsNullOrWhiteSpace(channelId))
            {
                _logger.LogError("Telegram log upload skipped: TelegramAlerts:ChannelId is not configured");
                return;
            }

            try
            {
                await using var stream = System.IO.File.OpenRead(filePath);
                var inputFile = new InputOnlineFile(stream, Path.GetFileName(filePath));

                await client.SendDocumentAsync(
                    chatId: channelId,
                    document: inputFile,
                    caption: caption,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send log file {FilePath} to Telegram channel", filePath);
            }
        }

        private TelegramBotClient? CreateClient()
        {
            if (string.IsNullOrWhiteSpace(_options.BotToken))
                return null;

            return new TelegramBotClient(_options.BotToken);
        }

        private static string EscapeMarkdown(string value) =>
            value
                .Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("[", "\\[");
    }
}
