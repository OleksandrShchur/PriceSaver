using System.Net;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Services
{
    public class NominatimGeocodingServiceTests
    {
        private static NominatimGeocodingService CreateSut(
            StubHttpMessageHandler handler,
            double minIntervalSeconds = 0.01)
        {
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://nominatim.test/")
            };

            var options = Microsoft.Extensions.Options.Options.Create(new NominatimOptions
            {
                BaseUrl = "https://nominatim.test",
                UserAgent = "PriceSaver-Tests/1.0",
                MinRequestIntervalSeconds = minIntervalSeconds,
                SearchResultLimit = 3
            });

            return new NominatimGeocodingService(
                httpClient,
                options,
                new TestLogger<NominatimGeocodingService>());
        }

        [Fact]
        public async Task SearchAsync_ParsesResults_AndBuildsQuery()
        {
            var body = """
                [
                  {"display_name":"Kyiv, Ukraine","lat":"50.4501","lon":"30.5234"},
                  {"display_name":"Kyiv Oblast, Ukraine","lat":"50.0","lon":"30.0"}
                ]
                """;
            var handler = StubHttpMessageHandler.WithBody(body);
            var sut = CreateSut(handler);

            var results = await sut.SearchAsync("Kyiv", CancellationToken.None);

            results.Should().HaveCount(2);
            results[0].DisplayName.Should().Be("Kyiv, Ukraine");
            results[0].Latitude.Should().Be(50.4501m);
            results[0].Longitude.Should().Be(30.5234m);
            handler.LastRequest!.RequestUri!.ToString().Should().Contain("search?");
            handler.LastRequest.RequestUri.ToString().Should().Contain("q=Kyiv");
            handler.LastRequest.RequestUri.ToString().Should().Contain("limit=3");
        }

        [Fact]
        public async Task SearchAsync_ReturnsEmpty_WhenNoMatches()
        {
            var handler = StubHttpMessageHandler.WithBody("[]");
            var sut = CreateSut(handler);

            var results = await sut.SearchAsync("zzzz-unknown", CancellationToken.None);

            results.Should().BeEmpty();
        }

        [Fact]
        public async Task ReverseAsync_ParsesDisplayName()
        {
            var body = """{"display_name":"Khreshchatyk, Kyiv","lat":"50.45","lon":"30.52"}""";
            var handler = StubHttpMessageHandler.WithBody(body);
            var sut = CreateSut(handler);

            var result = await sut.ReverseAsync(50.45m, 30.52m, CancellationToken.None);

            result.Should().NotBeNull();
            result!.DisplayName.Should().Be("Khreshchatyk, Kyiv");
            handler.LastRequest!.RequestUri!.ToString().Should().Contain("reverse?");
        }

        [Fact]
        public async Task SearchAsync_ReturnsEmpty_OnHttpError()
        {
            // 404 is not retried by the Polly policy (unlike 429/5xx).
            var handler = StubHttpMessageHandler.WithBody("oops", HttpStatusCode.NotFound);
            var sut = CreateSut(handler);

            var results = await sut.SearchAsync("Kyiv", CancellationToken.None);

            results.Should().BeEmpty();
        }

        [Fact]
        public async Task ConsecutiveRequests_AreSerializedByRateGate()
        {
            var callCount = 0;
            var handler = new StubHttpMessageHandler(_ =>
            {
                Interlocked.Increment(ref callCount);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
                };
            });

            var sut = CreateSut(handler, minIntervalSeconds: 0.05);

            await sut.SearchAsync("a", CancellationToken.None);
            await sut.SearchAsync("b", CancellationToken.None);

            callCount.Should().Be(2);
        }
    }
}
