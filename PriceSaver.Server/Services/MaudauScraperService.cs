using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Data;
using PriceSaver.Server.DTOs;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;

namespace PriceSaver.Server.Services
{
    public class MaudauScraperService : IMaudauScraperService
    {
        public const string HttpClientName = "Maudau";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MaudauOptions _options;
        private readonly ILogger<MaudauScraperService> _logger;

        public MaudauScraperService(
            IHttpClientFactory httpClientFactory,
            IServiceScopeFactory scopeFactory,
            IOptions<MaudauOptions> options,
            ILogger<MaudauScraperService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<MaudauScrapeResult> ScrapeAllAsync(CancellationToken cancellationToken = default)
        {
            var startPage = _options.StartPage;
            var maxPage = _options.EndPage;
            var batchSize = _options.MaxParallelRequests;

            _logger.LogInformation(
                "Maudau scrape started. Pages from {StartPage} (safety cap {MaxPage}), parallelism {Parallelism}.",
                startPage, maxPage, batchSize);

            var pagesProcessed = 0;
            var pagesFailed = 0;
            var productsInserted = 0;
            var productsUpdated = 0;
            var failedPages = new ConcurrentBag<int>();

            var reachedEnd = false;

            // Process pages in parallel batches, stopping once a page returns an empty
            // result. This makes the import independent from the site's product count.
            for (var firstPage = startPage; firstPage <= maxPage && !reachedEnd; firstPage += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var lastPage = Math.Min(firstPage + batchSize - 1, maxPage);
                var batch = Enumerable.Range(firstPage, lastPage - firstPage + 1);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = batchSize,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(batch, parallelOptions, async (page, ct) =>
                {
                    try
                    {
                        var products = await FetchPageAsync(page, ct);

                        if (products.Count == 0)
                        {
                            // An empty page marks the end of the catalog.
                            Volatile.Write(ref reachedEnd, true);
                            Interlocked.Increment(ref pagesProcessed);
                            _logger.LogInformation("Page {Page} is empty; reached end of catalog.", page);
                            return;
                        }

                        var (inserted, updated) = await UpsertPageAsync(products, ct);

                        Interlocked.Add(ref productsInserted, inserted);
                        Interlocked.Add(ref productsUpdated, updated);
                        Interlocked.Increment(ref pagesProcessed);

                        _logger.LogInformation(
                            "Page {Page} processed. Products: {Count} (inserted {Inserted}, updated {Updated}).",
                            page, products.Count, inserted, updated);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref pagesFailed);
                        failedPages.Add(page);
                        _logger.LogError(ex, "Page {Page} failed after retries and was skipped.", page);
                    }
                    finally
                    {
                        if (_options.DelayBetweenRequestsMs > 0)
                        {
                            await Task.Delay(_options.DelayBetweenRequestsMs, ct);
                        }
                    }
                });
            }

            if (!reachedEnd)
            {
                _logger.LogWarning(
                    "Reached safety cap page {MaxPage} before an empty page was found. Increase Maudau:EndPage if more pages exist.",
                    maxPage);
            }

            var result = new MaudauScrapeResult(
                pagesProcessed,
                pagesFailed,
                productsInserted,
                productsUpdated,
                failedPages.OrderBy(p => p).ToArray());

            _logger.LogInformation(
                "Maudau scrape finished. Pages processed {Processed}, failed {Failed}. Inserted {Inserted}, updated {Updated}.",
                result.PagesProcessed, result.PagesFailed, result.ProductsInserted, result.ProductsUpdated);

            if (result.FailedPages.Count > 0)
            {
                _logger.LogWarning("Failed pages: {FailedPages}", string.Join(", ", result.FailedPages));
            }

            return result;
        }

        private async Task<List<MaudauProductDto>> FetchPageAsync(int page, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var requestUri = $"{_options.BaseUrl}?page={page}";

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(requestUri, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var products = await response.Content
                        .ReadFromJsonAsync<List<MaudauProductDto>>(JsonOptions, cancellationToken);

                    return products ?? new List<MaudauProductDto>();
                }
                catch (Exception ex) when (ex is not OperationCanceledException && attempt < _options.MaxRetries)
                {
                    var backoff = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
                    _logger.LogWarning(
                        ex,
                        "Page {Page} request failed (attempt {Attempt}/{MaxRetries}). Retrying in {Backoff}ms.",
                        page, attempt + 1, _options.MaxRetries, backoff.TotalMilliseconds);

                    await Task.Delay(backoff, cancellationToken);
                }
            }
        }

        private async Task<(int Inserted, int Updated)> UpsertPageAsync(
            IReadOnlyCollection<MaudauProductDto> products,
            CancellationToken cancellationToken)
        {
            // A fresh scope (and DbContext) per page keeps the change tracker small and
            // avoids cross-thread DbContext usage during parallel page processing.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // De-duplicate by ProductId within the page (the API can repeat products across variations).
            var byProductId = products
                .Where(p => p.Id > 0)
                .GroupBy(p => p.Id)
                .ToDictionary(g => g.Key, g => g.Last());

            var productIds = byProductId.Keys.ToList();

            var existing = await db.MaudauProducts
                .Where(m => productIds.Contains(m.ProductId))
                .ToDictionaryAsync(m => m.ProductId, cancellationToken);

            var inserted = 0;
            var updated = 0;
            var now = DateTime.UtcNow;

            foreach (var (productId, dto) in byProductId)
            {
                if (existing.TryGetValue(productId, out var entity))
                {
                    MapToEntity(dto, entity, now);
                    updated++;
                }
                else
                {
                    var newEntity = new MaudauMarket { ProductId = productId };
                    MapToEntity(dto, newEntity, now);
                    db.MaudauProducts.Add(newEntity);
                    inserted++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            return (inserted, updated);
        }

        private static void MapToEntity(MaudauProductDto dto, MaudauMarket entity, DateTime timestampUtc)
        {
            entity.Slug = dto.Slug ?? string.Empty;
            entity.Title = dto.Title ?? string.Empty;
            entity.Price = ToMoney(dto.Offer?.Price ?? 0);
            entity.OldPrice = ToMoney(dto.Offer?.OldPrice ?? 0);
            entity.LastUpdatedUtc = timestampUtc;
        }

        /// <summary>
        /// Converts API money values (smallest currency unit) to decimal currency, e.g. 49400 -> 494.00.
        /// </summary>
        private static decimal ToMoney(long value) => value / 100m;
    }
}
