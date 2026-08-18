using PriceSaver.Server.Services;

namespace PriceSaver.Server.Tests.Helpers
{
    public sealed class RecordingTelegramAlertService : ITelegramAlertService
    {
        public List<(string FilePath, string Caption)> SentLogFiles { get; } = [];

        public bool SendLogFileResult { get; set; } = true;

        public Func<string, bool>? SendLogFilePredicate { get; set; }

        public Task SendErrorAlertAsync(string message, Exception? exception = null) =>
            Task.CompletedTask;

        public Task<bool> SendLogFileAsync(string filePath, string caption)
        {
            SentLogFiles.Add((filePath, caption));
            var result = SendLogFilePredicate?.Invoke(filePath) ?? SendLogFileResult;
            return Task.FromResult(result);
        }
    }
}
