using System.Net;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Services
{
    public class SilpoShortLinkResolverTests
    {
        private const string ShortUrl = "https://link.silpo.ua/e2451fad4255";
        private const string ProductId = "1f134686-841e-64fa-a6cc-ffc2b325d5fc";
        private const string ProductUrl = "https://silpo.ua/product/" + ProductId;
        private const string DeepLinksApiUrl =
            "https://sf-mobile-api.silpo.ua/v1/deep-links/e2451fad4255";

        private static SilpoShortLinkResolver CreateSut(StubHttpMessageHandler handler)
        {
            return new SilpoShortLinkResolver(
                new HttpClient(handler),
                new TestLogger<SilpoShortLinkResolver>());
        }

        [Theory]
        [InlineData("https://link.silpo.ua/e2451fad4255", true)]
        [InlineData("https://LINK.SILPO.UA/abc", true)]
        [InlineData("https://silpo.ua/product/foo-123", false)]
        [InlineData("https://www.silpo.ua/product/foo-123", false)]
        [InlineData("not-a-url", false)]
        public void NeedsResolve_MatchesSilpoShortHost(string url, bool expected)
        {
            var sut = CreateSut(StubHttpMessageHandler.WithBody("ok"));

            sut.NeedsResolve(url).Should().Be(expected);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsProductUrl_FromDeepLinksFallback()
        {
            var body =
                $$"""
                {"target":"https://link.silpo.ua?apn=ua.silpo.android&fallback=https%3A%2F%2Fsilpo.ua%2Fproduct%2F{{ProductId}}&productId={{ProductId}}&scheme=silpoua"}
                """;
            var handler = StubHttpMessageHandler.WithBody(body);
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().Be(ProductUrl);
            handler.LastRequest!.RequestUri!.ToString().Should().Be(DeepLinksApiUrl);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsProductUrl_FromProductId_WhenFallbackMissing()
        {
            var body =
                $$"""
                {"target":"https://link.silpo.ua?productId={{ProductId}}&scheme=silpoua"}
                """;
            var handler = StubHttpMessageHandler.WithBody(body);
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().Be(ProductUrl);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsNull_WhenStatusIsNotSuccess()
        {
            var handler = StubHttpMessageHandler.WithBody("gone", HttpStatusCode.NotFound);
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveAsync_ReturnsNull_WhenTargetHasNoProduct()
        {
            var body = """{"target":"https://link.silpo.ua?scheme=silpoua"}""";
            var handler = StubHttpMessageHandler.WithBody(body);
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveAsync_ReturnsNull_WhenFallbackIsNotSilpoProduct()
        {
            var body =
                """
                {"target":"https://link.silpo.ua?fallback=https%3A%2F%2Fexample.com%2Fproduct%2Ffoo"}
                """;
            var handler = StubHttpMessageHandler.WithBody(body);
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveAsync_ReturnsNull_WhenShortCodeMissing()
        {
            var handler = StubHttpMessageHandler.WithBody("{}");
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync("https://link.silpo.ua/", CancellationToken.None);

            result.Should().BeNull();
            handler.LastRequest.Should().BeNull();
        }
    }
}
