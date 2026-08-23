namespace PriceSaver.Server.Services
{
    public interface ISilpoShortLinkResolver
    {
        bool NeedsResolve(string url);

        /// <summary>
        /// Follows redirects for a Silpo short link and returns the final absolute URL,
        /// or null if resolution fails or the destination is not a Silpo product page.
        /// </summary>
        Task<string?> ResolveAsync(string url, CancellationToken cancellationToken = default);
    }
}
