using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using Telegram.Bot.Types;

namespace PriceSaver.Server.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public class TelegramController : ControllerBase
    {
        private const string TelegramSecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

        private readonly ITelegramUpdateHandler _updateHandler;
        private readonly TelegramOptions _options;

        public TelegramController(ITelegramUpdateHandler updateHandler, IOptions<TelegramOptions> options)
        {
            _updateHandler = updateHandler;
            _options = options.Value;
        }

        [HttpPost("callback")]
        public async Task<IActionResult> Callback([FromBody] Update update, CancellationToken cancellationToken)
        {
            if (!IsAuthorizedTelegramRequest())
            {
                return Unauthorized();
            }

            await _updateHandler.HandleAsync(update, cancellationToken);
            return Ok();
        }

        private bool IsAuthorizedTelegramRequest()
        {
            if (string.IsNullOrWhiteSpace(_options.SecretToken))
            {
                return true;
            }

            return Request.Headers.TryGetValue(TelegramSecretTokenHeader, out var secretToken) &&
                string.Equals(secretToken.ToString(), _options.SecretToken, StringComparison.Ordinal);
        }
    }
}
