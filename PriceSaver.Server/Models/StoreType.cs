using System.ComponentModel;

namespace PriceSaver.Server.Models
{
    public enum StoreType
    {
        [Description("АТБ")]
        ATB,

        [Description("Невідомий")]
        Unknown
    }
}
