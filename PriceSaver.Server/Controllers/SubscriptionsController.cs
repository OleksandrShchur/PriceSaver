using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Data;

namespace PriceSaver.Server.Controllers
{
    [ApiController]
    [Route("api/subscriptions")]
    public class SubscriptionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public SubscriptionsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet("user/{telegramId}")]
        public IActionResult GetForUser(long telegramId)
        {
            var subs = _db.Subscriptions.Where(s => s.UserId == telegramId).ToList();

            return Ok(subs);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var sub = await _db.Subscriptions.FindAsync(id);

            if (sub == null)
                return NotFound();

            sub.IsActive = false;
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
