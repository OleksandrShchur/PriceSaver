using Microsoft.Extensions.DependencyInjection;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Parsers
{
    public class ParserHttpClientRegistrationTests : IClassFixture<RealParsersWebApplicationFactory>
    {
        private readonly RealParsersWebApplicationFactory _factory;

        public ParserHttpClientRegistrationTests(RealParsersWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public void AllPriceParsers_ResolveFromDi_WithoutThrowing()
        {
            using var scope = _factory.Services.CreateScope();

            var parsers = scope.ServiceProvider.GetServices<IPriceParser>().ToList();

            parsers.Should().HaveCount(4);
            parsers.Select(p => p.StoreKey).Should().BeEquivalentTo("atb", "silpo", "maudau", "metro");
        }

        [Fact]
        public void SilpoAndMetro_HttpClients_HaveDistinctRefererHeaders()
        {
            using var scope = _factory.Services.CreateScope();
            var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

            var silpo = httpFactory.CreateClient(nameof(SilpoPriceParser));
            var metro = httpFactory.CreateClient(nameof(MetroPriceParser));

            silpo.DefaultRequestHeaders.Referrer.Should().Be(new Uri("https://silpo.ua/"));
            metro.DefaultRequestHeaders.Referrer.Should().Be(new Uri("https://shop.metro.ua/"));
        }
    }
}
