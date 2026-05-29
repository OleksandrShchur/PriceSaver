using System.Net;
using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Data;
using PriceSaver.Server.Parsers;

namespace PriceSaver.Server.Services
{
    public class PriceCheckerService
    {
        private readonly ApplicationDbContext _db;
        private readonly IPriceParser[] _parsers;
        private readonly ITelegramService _telegram;
        private readonly ILogger<PriceCheckerService> _logger;

        public PriceCheckerService(ApplicationDbContext db, IEnumerable<IPriceParser> parsers, ITelegramService telegram, ILogger<PriceCheckerService> logger)
        {
            _db = db;
            _parsers = parsers.ToArray();
            _telegram = telegram;
            _logger = logger;
        }

        public async Task CheckAllAsync(CancellationToken ct = default)
        {
            var subs = await _db.Subscriptions.Where(s => s.IsActive).ToListAsync(ct);

            foreach (var sub in subs)
            {
                try
                {
                    var parser = _parsers.FirstOrDefault(p => p.CanParse(sub.ProductUrl));
                    if (parser == null)
                    {
                        _logger.LogWarning("No parser for {Url}", sub.ProductUrl);
                        continue;
                    }

                    var (name, price) = await parser.ParseAsync(sub.ProductUrl, ct);
                    if (price != sub.CurrentPrice)
                    {
                        var old = sub.CurrentPrice;
                        sub.CurrentPrice = price;
                        sub.LastCheckedDate = DateTime.UtcNow;

                        _db.PriceHistories.Add(new Models.PriceHistory
                        {
                            SubscriptionId = sub.Id,
                            Price = price,
                            CheckedAt = DateTime.UtcNow
                        });

                        await _db.SaveChangesAsync(ct);

                        var percent = old == 0 ? 100 : Math.Round((double)((price - old) / old * 100M), 2);
                        var safeName = WebUtility.HtmlEncode(name);

                        if (price > old && sub.NotifyOnIncrease)
                        {
                            var text = $"📈 <b>Ціна зросла!</b>\n\n" +
                                       $"📦 <b>{safeName}</b>\n" +
                                       $"💰 <code>{old:0.##}</code> UAH → <code>{price:0.##}</code> UAH (<code>+{percent}%</code>)\n\n" +
                                       $"🔗 <a href=\"{sub.ProductUrl}\">Перейти до товару</a>";

                            await _telegram.SendMessageAsync(
                                sub.UserId,
                                text,
                                cancellationToken: ct);
                        }
                        else if (price < old)
                        {
                            var text = $"📉 <b>Ціна знизилася!</b>\n\n" +
                                       $"📦 <b>{safeName}</b>\n" +
                                       $"💰 <code>{old:0.##}</code> UAH → <code>{price:0.##}</code> UAH (<code>{percent}%</code>)\n\n" +
                                       $"🔗 <a href=\"{sub.ProductUrl}\">Перейти до товару</a>";

                            await _telegram.SendMessageAsync(
                                sub.UserId,
                                text,
                                cancellationToken: ct);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check price for {Url}", sub.ProductUrl);
                }

                // small delay to be polite to stores
                await Task.Delay(500, ct);
            }
        }
    }
}
