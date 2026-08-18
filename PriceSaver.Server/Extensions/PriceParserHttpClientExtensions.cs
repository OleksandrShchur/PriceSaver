using PriceSaver.Server.Parsers;

namespace PriceSaver.Server.Extensions
{
    internal static class PriceParserHttpClientExtensions
    {
        /// <summary>
        /// Registers <typeparamref name="TParser"/> as its own typed HttpClient
        /// (named after the concrete type) and exposes it as <see cref="IPriceParser"/>.
        /// Do not use <c>AddHttpClient&lt;IPriceParser, TParser&gt;</c>: every parser
        /// would share the name "IPriceParser" and their DefaultRequestHeaders collide.
        /// </summary>
        public static IHttpClientBuilder AddPriceParserHttpClient<TParser>(
            this IServiceCollection services,
            Action<HttpClient>? configureClient = null)
            where TParser : class, IPriceParser
        {
            var builder = configureClient is null
                ? services.AddHttpClient<TParser>()
                : services.AddHttpClient<TParser>(configureClient);

            services.AddTransient<IPriceParser>(sp => sp.GetRequiredService<TParser>());
            return builder;
        }
    }
}
