using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Services;
using Telegram.Bot.Types;

namespace PriceSaver.Server.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public class TelegramController : ControllerBase
    {
        private readonly ITelegramUpdateHandler _updateHandler;

        public TelegramController(ITelegramUpdateHandler updateHandler)
        {
            _updateHandler = updateHandler;
        }

        [HttpPost("callback")]
        public async Task<IActionResult> Callback([FromBody] Update update, CancellationToken cancellationToken)
        {
            await _updateHandler.HandleAsync(update, cancellationToken);

            return Ok();
        }
    }
}
