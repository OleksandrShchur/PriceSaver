using System.ComponentModel.DataAnnotations;

namespace PriceSaver.Server.Options
{
    public class MaudauOptions
    {
        public const string SectionName = "Maudau";

        /// <summary>
        /// Base products search endpoint. The page number is appended via the <c>page</c> query parameter.
        /// </summary>
        [Required]
        public string BaseUrl { get; set; } = "https://backend.prod.maudau.click/v1/user/products/searches";

        /// <summary>
        /// First page index to fetch (inclusive).
        /// </summary>
        [Range(0, int.MaxValue)]
        public int StartPage { get; set; } = 0;

        /// <summary>
        /// Safety cap on the highest page index to fetch (inclusive). Scraping normally
        /// stops earlier, as soon as a page returns an empty result. This cap only guards
        /// against an unbounded loop if the API never returns an empty page.
        /// </summary>
        [Range(0, int.MaxValue)]
        public int EndPage { get; set; } = 5000;

        /// <summary>
        /// Maximum number of pages fetched concurrently.
        /// </summary>
        [Range(1, 64)]
        public int MaxParallelRequests { get; set; } = 4;

        /// <summary>
        /// Delay (in milliseconds) applied after each request to avoid rate limiting.
        /// </summary>
        [Range(0, 60000)]
        public int DelayBetweenRequestsMs { get; set; } = 250;

        /// <summary>
        /// Number of retry attempts for a failed request.
        /// </summary>
        [Range(0, 10)]
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// HTTP request timeout in seconds.
        /// </summary>
        [Range(1, 600)]
        public int RequestTimeoutSeconds { get; set; } = 30;
    }
}
