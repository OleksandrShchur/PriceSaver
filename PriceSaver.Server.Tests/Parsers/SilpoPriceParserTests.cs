using System.Net;
using PriceSaver.Server.Models;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Parsers
{
    public class SilpoPriceParserTests
    {
        private static SilpoPriceParser CreateParser(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var handler = StubHttpMessageHandler.WithBody(body, status);
            return new SilpoPriceParser(new HttpClient(handler));
        }

        [Theory]
        [InlineData("https://silpo.ua/product/moloko-123", true)]
        [InlineData("https://www.silpo.ua/product/moloko-123", true)]
        [InlineData("https://atbmarket.com/product/1", false)]
        [InlineData("garbage", false)]
        public void CanParse_MatchesSilpoHosts(string url, bool expected)
        {
            var parser = CreateParser("{}");
            parser.CanParse(url).Should().Be(expected);
            parser.StoreKey.Should().Be("silpo");
            parser.StoreType.Should().Be(StoreType.Silpo);
        }

        [Fact]
        public async Task ParseAsync_ReturnsTitleAndPriceToShow()
        {
            const string json = """
            { "title": "Молоко Яготинське 2.6% 900г", "price": 45.90, "priceToShow": 42.50 }
            """;
            var parser = CreateParser(json);

            var (name, price) = await parser.ParseAsync("https://silpo.ua/product/moloko-yagotynske-900g");

            name.Should().Be("Молоко Яготинське 2.6% 900г");
            price.Should().Be(42.50m);
        }

        [Fact]
        public async Task ParseAsync_FallsBackToRegularPrice_WhenNoPriceToShow()
        {
            const string json = """
            { "name": "Сир кисломолочний", "price": 89.99 }
            """;
            var parser = CreateParser(json);

            var (name, price) = await parser.ParseAsync("https://silpo.ua/product/syr-123");

            name.Should().Be("Сир кисломолочний");
            price.Should().Be(89.99m);
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenProductNotFound()
        {
            var parser = CreateParser("{}", HttpStatusCode.NotFound);

            var act = async () => await parser.ParseAsync("https://silpo.ua/product/gone-123");

            await act.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task ParseAsync_UsesDiscountPrice_WhenOnlyDiscountPresent()
        {
            const string json = """
            { "title": "Печиво вівсяне", "discount": 30.00 }
            """;
            var parser = CreateParser(json);

            var (name, price) = await parser.ParseAsync("https://silpo.ua/product/pechyvo-1");

            name.Should().Be("Печиво вівсяне");
            price.Should().Be(30.00m);
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenUrlHasNoProductSlug()
        {
            var parser = CreateParser("{}");

            var act = async () => await parser.ParseAsync("https://silpo.ua/category/dairy");

            await act.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenNoPriceFieldsPresent()
        {
            const string json = """
            { "title": "Без ціни" }
            """;
            var parser = CreateParser(json);

            var act = async () => await parser.ParseAsync("https://silpo.ua/product/no-price");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
