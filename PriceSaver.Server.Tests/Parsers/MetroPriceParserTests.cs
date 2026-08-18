using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using PriceSaver.Server.Models;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Parsers
{
    public class MetroPriceParserTests
    {
        private const string FixtureJson = """
            {
              "result": {
                "BTY-X395449": {
                  "variants": {
                    "0032": {
                      "description": "METRO Chef Вершки ультрапастеризовані 20% 1л",
                      "bundles": {
                        "0021": {
                          "sellingPriceInfo": {
                            "finalPrice": 155.00
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        private static MetroPriceParser CreateParser(
            string body,
            HttpStatusCode status,
            out StubHttpMessageHandler handler)
        {
            handler = StubHttpMessageHandler.WithBody(body, status);
            return new MetroPriceParser(new HttpClient(handler), NullLogger<MetroPriceParser>.Instance);
        }

        private static MetroPriceParser CreateParser(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            CreateParser(body, status, out _);

        [Theory]
        [InlineData("https://shop.metro.ua/shop/pv/BTY-X288383/0032/0021", true)]
        [InlineData("https://shop.metro.ua/shop/pv/BTY-X9528/0032/0021/%D0%AF%D1%82%D1%80%D0%B0%D0%BD%D1%8C-%D0%A8%D0%B8%D0%BD%D0%BA%D0%B0", true)]
        [InlineData("https://shop.metro.ua/", true)]
        [InlineData("https://www.shop.metro.ua/shop/pv/BTY-X395449/0032/0021", true)]
        [InlineData("https://silpo.ua/product/1", false)]
        [InlineData("garbage", false)]
        public void CanParse_MatchesMetroHosts(string url, bool expected)
        {
            var parser = CreateParser("{}");
            parser.CanParse(url).Should().Be(expected);
            parser.StoreKey.Should().Be("metro");
            parser.StoreType.Should().Be(StoreType.Metro);
        }

        [Fact]
        public async Task ParseAsync_ReturnsDescriptionAndFinalPrice()
        {
            var parser = CreateParser(FixtureJson, HttpStatusCode.OK, out var handler);
            const string url = "https://shop.metro.ua/shop/pv/BTY-X395449/0032/0021";

            var (name, price) = await parser.ParseAsync(url);

            name.Should().Be("METRO Chef Вершки ультрапастеризовані 20% 1л");
            price.Should().Be(155.00m);
            handler.LastRequest.Should().NotBeNull();
            handler.LastRequest!.RequestUri!.ToString().Should().Contain("ids=BTY-X395449");
            handler.LastRequest.RequestUri.ToString().Should().Contain("storeIds=00027");
        }

        [Fact]
        public async Task ParseAsync_IgnoresTrailingSlug_WhenExtractingIds()
        {
            const string json = """
            {
              "result": {
                "BTY-X9528": {
                  "variants": {
                    "0032": {
                      "description": "Ятрань Шинка Ювілейна",
                      "bundles": {
                        "0021": {
                          "sellingPriceInfo": {
                            "finalPrice": 199.90
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
            var parser = CreateParser(json, HttpStatusCode.OK, out var handler);
            const string url =
                "https://shop.metro.ua/shop/pv/BTY-X9528/0032/0021/%D0%AF%D1%82%D1%80%D0%B0%D0%BD%D1%8C-%D0%A8%D0%B8%D0%BD%D0%BA%D0%B0";

            var (name, price) = await parser.ParseAsync(url);

            name.Should().Be("Ятрань Шинка Ювілейна");
            price.Should().Be(199.90m);
            handler.LastRequest!.RequestUri!.ToString().Should().Contain("ids=BTY-X9528");
            handler.LastRequest.RequestUri.ToString().Should().Contain("storeIds=00027");
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenProductNotFound()
        {
            var parser = CreateParser("{}", HttpStatusCode.NotFound);

            var act = async () => await parser.ParseAsync(
                "https://shop.metro.ua/shop/pv/BTY-X395449/0032/0021");

            await act.Should().ThrowAsync<PriceParseException>();
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenUrlHasNoProductPath()
        {
            var parser = CreateParser("{}");

            var act = async () => await parser.ParseAsync("https://shop.metro.ua/");

            await act.Should().ThrowAsync<PriceParseException>();
        }

        [Fact]
        public async Task ParseAsync_Throws_WhenFinalPriceMissing()
        {
            const string json = """
            {
              "result": {
                "BTY-X395449": {
                  "variants": {
                    "0032": {
                      "description": "METRO Chef Вершки ультрапастеризовані 20% 1л",
                      "bundles": {
                        "0021": {
                          "sellingPriceInfo": {}
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
            var parser = CreateParser(json);

            var act = async () => await parser.ParseAsync(
                "https://shop.metro.ua/shop/pv/BTY-X395449/0032/0021");

            await act.Should().ThrowAsync<PriceParseException>();
        }
    }
}
