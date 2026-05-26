using System.Threading.Tasks;

namespace PriceSaver.Server.Services
{
    public interface ITelegramService
    {
        Task SendMessageAsync(long chatId, string text);
    }
}
