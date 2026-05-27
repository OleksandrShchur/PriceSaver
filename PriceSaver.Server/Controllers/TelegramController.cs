using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using PriceSaver.Server.Handlers;
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

        [HttpPost]
        public async Task<IActionResult> Post(CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var update = JsonConvert.DeserializeObject<Update>(body);

            if (update is null)
            {
                return BadRequest("Invalid Telegram update payload.");
            }

            await _updateHandler.HandleAsync(update, cancellationToken);

            return Ok();
        }

        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { status = "Telegram bot is running" });
        }
    }
}
