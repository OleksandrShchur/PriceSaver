using PriceSaver.Server.Services;

namespace PriceSaver.Server.Tests.Helpers
{
    public sealed class NoOpTelegramAlertService : ITelegramAlertService
    {
        public Task SendErrorAlertAsync(string message, Exception? exception = null) =>
            Task.CompletedTask;

        public Task<bool> SendLogFileAsync(string filePath, string caption) =>
            Task.FromResult(true);
    }
}
