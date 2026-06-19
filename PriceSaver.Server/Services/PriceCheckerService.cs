using System.Text;
using PriceSaver.Server.Models;
using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Data;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Parsers;

namespace PriceSaver.Server.Services
{
    public class PriceCheckerService
    {
        private const int MaxProductTitleLength = 45;

        private sealed record PriceChangeRow(
            string ProductName,
            string ProductUrl,
            decimal OldPrice,
            decimal NewPrice,
            string ChangePercentText);

        private static string EscapeMarkdownTableCell(string value)
        {
            // Telegram parses GFM-like Markdown tables in rich messages.
            // To keep cell content from breaking the table structure, we escape pipes.
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static string EscapeMarkdownLinkText(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static string TruncateProductTitle(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= MaxProductTitleLength)
            {
                return text;
            }

            return text[..(MaxProductTitleLength - 1)].TrimEnd() + "…";
        }

        private static string FormatProductCell(string productName, string productUrl)
        {
            var title = EscapeMarkdownLinkText(TruncateProductTitle(productName));
            return $"[{title}]({productUrl})";
        }

        private static string BuildMarketPriceChangesMarkdown(string marketName, IReadOnlyList<PriceChangeRow> rows, int partIndex, int partsCount)
        {
            var sb = new StringBuilder();

            var marketSuffix = partsCount > 1 ? $" (частина {partIndex + 1}/{partsCount})" : string.Empty;
            sb.AppendLine($"🏪 **{marketName}**{marketSuffix}");
            sb.AppendLine();

            // Column alignment keeps numeric columns compact so long product titles
            // do not push price columns off-screen on mobile clients.
            sb.AppendLine("| Товар | Стара ціна | Нова ціна | Зміна (%) |");
            sb.AppendLine("|:------|-----------:|----------:|----------:|");

            foreach (var row in rows)
            {
                sb.AppendLine(
                    $"| {FormatProductCell(row.ProductName, row.ProductUrl)} | {row.OldPrice:0.##} | {row.NewPrice:0.##} | {EscapeMarkdownTableCell(row.ChangePercentText)} |");
            }

            return sb.ToString().TrimEnd();
        }

        private readonly ApplicationDbContext _db;
        private readonly IPriceParser[] _parsers;
        private readonly ITelegramService _telegram;
        private readonly IChannelPostService _channelPostService;
        private readonly ILogger<PriceCheckerService> _logger;

        public PriceCheckerService(
            ApplicationDbContext db,
            IEnumerable<IPriceParser> parsers,
            ITelegramService telegram,
            IChannelPostService channelPostService,
            ILogger<PriceCheckerService> logger)
        {
            _db = db;
            _parsers = parsers.ToArray();
            _telegram = telegram;
            _channelPostService = channelPostService;
            _logger = logger;
        }

        public async Task CheckAllAsync(CancellationToken ct = default)
        {
            var subs = await _db.Subscriptions.Where(s => s.IsActive).ToListAsync(ct);

            _logger.LogInformation("Price check cycle started. Total active subscriptions: {Count}", subs.Count);

            // Scenario 1: when a user receives price changes, send separate message per market.
            // Additionally, each message may contain not more than 1 table, and each table not more than 10 rows.
            var changesByUserAndMarket = new Dictionary<(long userId, StoreType storeType), List<PriceChangeRow>>();
            var channelDropsByMarket = new Dictionary<StoreType, List<PriceChangeRow>>();

            var productGroups = subs.GroupBy(s => (s.ProductUrl, s.StoreType));

            foreach (var group in productGroups)
            {
                var productUrl = group.Key.ProductUrl;

                try
                {
                    var parser = _parsers.FirstOrDefault(p => p.CanParse(productUrl));
                    if (parser == null)
                    {
                        _logger.LogWarning("No parser for {Url}", productUrl);
                        continue;
                    }

                    var (name, price) = await parser.ParseAsync(productUrl, ct);
                    var checkedAt = DateTime.UtcNow;
                    var hasChanges = false;

                    foreach (var sub in group)
                    {
                        if (price == sub.CurrentPrice)
                            continue;

                        var old = sub.CurrentPrice;
                        sub.CurrentPrice = price;
                        sub.LastCheckedDate = checkedAt;

                        _db.PriceHistories.Add(new Models.PriceHistory
                        {
                            SubscriptionId = sub.Id,
                            Price = price,
                            CheckedAt = checkedAt
                        });

                        hasChanges = true;

                        var percent = old == 0 ? 100 : Math.Round((double)((price - old) / old * 100M), 2);
                        var changePercentText = price > old
                            ? $"+{percent:0.##}%"
                            : $"{percent:0.##}%";

                        var shouldNotify =
                            (price > old && sub.NotifyOnIncrease) ||
                            (price < old);

                        if (shouldNotify)
                        {
                            if (price < old)
                            {
                                _logger.LogInformation(
                                    "Price drop detected for SubscriptionId: {Id}. OldPrice: {Old} UAH → NewPrice: {New} UAH. Notifying UserId: {UserId}",
                                    sub.Id,
                                    old,
                                    price,
                                    sub.UserId);
                            }

                            var key = (sub.UserId, sub.StoreType);
                            if (!changesByUserAndMarket.TryGetValue(key, out var list))
                            {
                                list = new List<PriceChangeRow>();
                                changesByUserAndMarket[key] = list;
                            }

                            list.Add(new PriceChangeRow(name, sub.ProductUrl, old, price, changePercentText));

                            if (price < old && _channelPostService.IsEnabled)
                            {
                                if (!channelDropsByMarket.TryGetValue(sub.StoreType, out var channelList))
                                {
                                    channelList = new List<PriceChangeRow>();
                                    channelDropsByMarket[sub.StoreType] = channelList;
                                }

                                channelList.Add(new PriceChangeRow(name, sub.ProductUrl, old, price, changePercentText));
                            }
                        }
                    }

                    if (hasChanges)
                        await _db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check price for {Url}", productUrl);
                }

                // small delay to be polite to stores
                await Task.Delay(500, ct);
            }

            // Flush messages after all subscriptions are checked, so the user receives one message per market
            // (possibly chunked into multiple messages if there are more than 10 changes for that market).
            foreach (var entry in changesByUserAndMarket)
            {
                var (userId, storeType) = entry.Key;
                var marketName = storeType.GetDescription();
                var rows = entry.Value;

                var partsCount = (rows.Count + 9) / 10;
                var partIndex = 0;
                for (var i = 0; i < rows.Count; i += 10)
                {
                    var chunkSize = Math.Min(10, rows.Count - i);
                    var chunk = rows.GetRange(i, chunkSize);

                    var markdown = BuildMarketPriceChangesMarkdown(marketName, chunk, partIndex, partsCount);
                    await _telegram.SendRichMessageAsync(
                        userId,
                        markdown,
                        cancellationToken: ct);

                    partIndex++;
                }
            }

            if (channelDropsByMarket.Count > 0)
            {
                var channelMarkdown = BuildChannelDropsMarkdown(channelDropsByMarket);
                await _channelPostService.SubmitForApprovalAsync(channelMarkdown, ct);
            }
        }

        private static string BuildChannelDropsMarkdown(Dictionary<StoreType, List<PriceChangeRow>> dropsByMarket)
        {
            var sb = new StringBuilder();
            sb.AppendLine("📉 **Зниження цін**");
            sb.AppendLine();

            foreach (var entry in dropsByMarket.OrderBy(e => e.Key))
            {
                var marketName = entry.Key.GetDescription();
                var rows = entry.Value;
                var partsCount = (rows.Count + 9) / 10;
                var partIndex = 0;

                for (var i = 0; i < rows.Count; i += 10)
                {
                    var chunkSize = Math.Min(10, rows.Count - i);
                    var chunk = rows.GetRange(i, chunkSize);
                    sb.AppendLine(BuildMarketPriceChangesMarkdown(marketName, chunk, partIndex, partsCount));
                    sb.AppendLine();
                    partIndex++;
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
