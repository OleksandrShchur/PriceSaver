namespace PriceSaver.Server.Services
{
    public interface IMaudauScraperService
    {
        /// <summary>
        /// Fetches every configured page from the Maudau products search API,
        /// parses the products and upserts them into the database.
        /// </summary>
        Task<MaudauScrapeResult> ScrapeAllAsync(CancellationToken cancellationToken = default);
    }

    public record MaudauScrapeResult(
        int PagesProcessed,
        int PagesFailed,
        int ProductsInserted,
        int ProductsUpdated,
        IReadOnlyCollection<int> FailedPages);
}
