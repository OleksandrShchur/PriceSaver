using System.Net;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Services
{
    public class SilpoShortLinkResolverTests
    {
        private const string ShortUrl = "https://link.silpo.ua/e2451fad4255";
        private const string ProductUrl =
            "https://silpo.ua/product/pechyvo-oreo-z-kakao-ta-nachynkoiu-vanilnogo-smaku-1023011";

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
        public async Task ResolveAsync_ReturnsFinalProductUrl_WhenRedirectSucceeded()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, ProductUrl)
            });
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().Be(ProductUrl);
            handler.LastRequest!.RequestUri!.ToString().Should().Be(ShortUrl);
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
        public async Task ResolveAsync_ReturnsNull_WhenFinalUrlIsNotSilpoProduct()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://silpo.ua/category/dairy")
            });
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolveAsync_ReturnsNull_WhenFinalHostIsNotSilpo()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/product/foo")
            });
            var sut = CreateSut(handler);

            var result = await sut.ResolveAsync(ShortUrl, CancellationToken.None);

            result.Should().BeNull();
        }
    }
}
