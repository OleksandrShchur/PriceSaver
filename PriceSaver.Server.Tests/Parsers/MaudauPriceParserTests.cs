using System.Net;
using PriceSaver.Server.Models;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Parsers
{
    public class MaudauPriceParserTests
    {
        private static MaudauPriceParser CreateParser(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var handler = StubHttpMessageHandler.WithBody(body, status);
            return new MaudauPriceParser(new HttpClient(handler));
        }

        [Theory]
        [InlineData("https://maudau.com.ua/product/chipsy-123", true)]
        [InlineData("https://www.maudau.com.ua/product/chipsy-123", true)]
        [InlineData("https://silpo.ua/product/1", false)]
        [InlineData("???", false)]
        public void CanParse_MatchesMaudauHosts(string url, bool expected)
        {
            var parser = CreateParser("{}");
            parser.CanParse(url).Should().Be(expected);
            parser.StoreKey.Should().Be("maudau");
            parser.StoreType.Should().Be(StoreType.Maudau);
        }

        [Fact]
        public async Task ParseAsync_ReturnsTitleAndPrice_FromNestedResponse()
        {
            const string json = """
            { "data": { "products": [ { "title": "Чіпси Lays 133г", "priceToShow": 150.50 } ] } }
            """;
            var parser = CreateParser(json);

            var (name, price) = await parser.ParseAsync("https://maudau.com.ua/product/chipsy-lays-133g");

            name.Should().Be("Чіпси Lays 133г");
            price.Should().Be(150.50m);
        }

        [Fact]
        public async Task ParseAsync_NormalizesCentsToUnits()
        {
            const string json = """
            { "title": "Кава мелена 250г", "priceToShow": 9900 }
            """;
            var parser = CreateParser(json);

            var (name, price) = await parser.ParseAsync("https://maudau.com.ua/product/kava-250g");

            name.Should().Be("Кава мелена 250г");
            price.Should().Be(99m);
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenProductNotFound()
        {
            var parser = CreateParser("{}", HttpStatusCode.NotFound);

            var act = async () => await parser.ParseAsync("https://maudau.com.ua/product/gone-123");

            await act.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task ParseAsync_ReadsPrice_FromTopLevelArray()
        {
            const string json = """
            [ { "name": "Сік яблучний 1л", "price": 38.90 } ]
            """;
            var parser = CreateParser(json);

            var (name, price) = await parser.ParseAsync("https://maudau.com.ua/product/sik-1l");

            name.Should().Be("Сік яблучний 1л");
            price.Should().Be(38.90m);
        }

        [Fact]
        public async Task ParseAsync_ReadsPrice_FromOfferObject()
        {
            const string json = """
            { "product": { "productName": "Шоколад чорний", "offer": { "price": 64.50 } } }
            """;
            var parser = CreateParser(json);

            var (name, price) = await parser.ParseAsync("https://maudau.com.ua/product/shokolad-1");

            name.Should().Be("Шоколад чорний");
            price.Should().Be(64.50m);
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenPriceMissing()
        {
            const string json = """
            { "data": { "products": [ { "title": "Товар без ціни" } ] } }
            """;
            var parser = CreateParser(json);

            var act = async () => await parser.ParseAsync("https://maudau.com.ua/product/no-price");

            await act.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenUrlHasNoProductSlug()
        {
            var parser = CreateParser("{}");

            var act = async () => await parser.ParseAsync("https://maudau.com.ua/category/snacks");

            await act.Should().ThrowAsync<Exception>();
        }
    }
}
