using System.ComponentModel;
using System.Reflection;
using PriceSaver.Server.Models;

namespace PriceSaver.Server.Extensions
{
    public static class StoreTypeEnumExtensions
    {
        public static string GetApiLabel(this StoreType storeType) => storeType switch
        {
            StoreType.ATB => "ATB",
            StoreType.Silpo => "Silpo",
            StoreType.Maudau => "Maudau",
            StoreType.Metro => "METRO",
            _ => storeType.ToString()
        };

        public static string GetDescription(this Enum value)
        {
            if (value == null)
                return string.Empty;

            var field = value.GetType().GetField(value.ToString());

            if (field == null)
                return value.ToString();

            var attribute = field.GetCustomAttribute<DescriptionAttribute>();

            return attribute?.Description ?? value.ToString();
        }
    }
}
