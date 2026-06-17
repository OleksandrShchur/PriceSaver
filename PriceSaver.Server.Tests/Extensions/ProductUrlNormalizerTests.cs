using PriceSaver.Server.Extensions;

namespace PriceSaver.Server.Tests.Extensions
{
    public class ProductUrlNormalizerTests
    {
        [Theory]
        [InlineData("https://WWW.ATBmarket.com/product/1/", "https://www.atbmarket.com/product/1")]
        [InlineData("https://www.atbmarket.com/product/1#details", "https://www.atbmarket.com/product/1")]
        [InlineData("  https://www.atbmarket.com/product/1  ", "https://www.atbmarket.com/product/1")]
        public void Normalize_CanonicalizesUrl(string input, string expected)
        {
            ProductUrlNormalizer.Normalize(input).Should().Be(expected);
        }
    }
}
