using PriceSaver.Server.Extensions;
using PriceSaver.Server.Models;

namespace PriceSaver.Server.Tests.Extensions
{
    public class StoreTypeEnumExtensionsTests
    {
        [Theory]
        [InlineData(StoreType.ATB, "АТБ")]
        [InlineData(StoreType.Silpo, "Сільпо")]
        [InlineData(StoreType.Maudau, "Maudau")]
        [InlineData(StoreType.Unknown, "Невідомий")]
        public void GetDescription_ReturnsLocalizedDescription(StoreType value, string expected)
        {
            value.GetDescription().Should().Be(expected);
        }

        [Fact]
        public void GetDescription_ReturnsEnumName_WhenNoDescriptionAttribute()
        {
            // DeactivateSubscriptionStatus values have no [Description] attribute.
            DeactivateSubscriptionStatus.Success.GetDescription().Should().Be("Success");
        }

        [Fact]
        public void GetDescription_ReturnsValueString_ForUndefinedEnumValue()
        {
            ((StoreType)999).GetDescription().Should().Be("999");
        }

        [Fact]
        public void GetDescription_ReturnsEmpty_WhenValueNull()
        {
            StoreTypeEnumExtensions.GetDescription(null!).Should().BeEmpty();
        }
    }
}
