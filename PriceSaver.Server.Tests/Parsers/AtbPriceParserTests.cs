using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PriceSaver.Server.Models;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Parsers
{
    public class AtbPriceParserTests
    {
        private static AtbPriceParser CreateParser(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var handler = StubHttpMessageHandler.WithBody(body, status, "text/plain");
            return new AtbPriceParser(new HttpClient(handler), NullLogger<AtbPriceParser>.Instance);
        }

        [Theory]
        [InlineData("https://www.atbmarket.com/product/123", true)]
        [InlineData("https://atbmarket.com/product/123", true)]
        [InlineData("https://atb.ua/product/123", true)]
        [InlineData("https://silpo.ua/product/123", false)]
        [InlineData("not a url", false)]
        public void CanParse_MatchesAtbHosts(string url, bool expected)
        {
            var parser = CreateParser("ignored");
            parser.CanParse(url).Should().Be(expected);
            parser.StoreKey.Should().Be("atb");
            parser.StoreType.Should().Be(StoreType.ATB);
        }

        [Fact]
        public async Task ParseAsync_ReturnsTitleAndCardPrice()
        {
            const string markdown = """
            Title: Some Page

            # Молоко Селянське 2.5% 900г

            Опис товару
            199.99 з карткою АТБ
            210.50 грн /шт
            """;
            var parser = CreateParser(markdown);

            var (name, price) = await parser.ParseAsync("https://www.atbmarket.com/product/123");

            name.Should().Be("Молоко Селянське 2.5% 900г");
            price.Should().Be(199.99m);
        }

        [Fact]
        public async Task ParseAsync_FallsBackToRegularPrice_WhenNoCardPrice()
        {
            const string markdown = """
            # Хліб Бородинський 400г

            263.50 грн /шт
            """;
            var parser = CreateParser(markdown);

            var (name, price) = await parser.ParseAsync("https://www.atbmarket.com/product/777");

            name.Should().Be("Хліб Бородинський 400г");
            price.Should().Be(263.50m);
        }

        [Fact]
        public async Task ParseAsync_HandlesCommaDecimalSeparator()
        {
            const string markdown = """
            # Кефір 1%

            55,49 з карткою АТБ
            """;
            var parser = CreateParser(markdown);

            var (_, price) = await parser.ParseAsync("https://www.atbmarket.com/product/55");

            price.Should().Be(55.49m);
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenJinaReportsNotFound()
        {
            const string markdown = "Warning: Target URL returned error 404: Not Found";
            var parser = CreateParser(markdown);

            var act = async () => await parser.ParseAsync("https://www.atbmarket.com/product/missing");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
