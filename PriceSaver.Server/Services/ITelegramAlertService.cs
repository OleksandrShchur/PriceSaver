namespace PriceSaver.Server.Services
{
    public interface ITelegramAlertService
    {
        Task SendErrorAlertAsync(string message, Exception? exception = null);
        Task SendLogFileAsync(string filePath, string caption);
    }
}
