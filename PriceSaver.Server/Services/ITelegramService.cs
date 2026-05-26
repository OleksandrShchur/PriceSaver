namespace PriceSaver.Server.Services
{
    public interface ITelegramService
    {
        Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
    }
}
