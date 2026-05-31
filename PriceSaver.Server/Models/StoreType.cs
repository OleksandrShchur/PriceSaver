using System.ComponentModel;

namespace PriceSaver.Server.Models
{
    public enum StoreType
    {
        [Description("АТБ")]
        ATB,

        [Description("Сільпо")]
        Silpo,

        [Description("Невідомий")]
        Unknown
    }
}
