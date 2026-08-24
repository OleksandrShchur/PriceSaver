namespace PriceSaver.Server.Services
{
    public interface ISilpoShortLinkResolver
    {
        bool NeedsResolve(string url);

        /// <summary>
        /// Resolves a Silpo app short link via the deep-links API to a silpo.ua/product URL,
        /// or null if resolution fails or the destination is not a Silpo product page.
        /// </summary>
        Task<string?> ResolveAsync(string url, CancellationToken cancellationToken = default);
    }
}
