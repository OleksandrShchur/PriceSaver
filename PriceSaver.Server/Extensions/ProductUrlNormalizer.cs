namespace PriceSaver.Server.Extensions
{
    public static class ProductUrlNormalizer
    {
        public static string Normalize(string url)
        {
            var trimmed = url.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return trimmed;

            var builder = new UriBuilder(uri)
            {
                Scheme = uri.Scheme.ToLowerInvariant(),
                Host = uri.Host.ToLowerInvariant(),
                Fragment = string.Empty,
                Port = uri.IsDefaultPort ? -1 : uri.Port
            };

            var path = builder.Path;
            if (path.Length > 1 && path.EndsWith('/'))
                builder.Path = path[..^1];

            return builder.Uri.ToString();
        }
    }
}
