namespace PriceSaver.Server.Helpers
{
    /// <summary>
    /// Shared helpers for GFM-like Markdown used in Telegram rich messages.
    /// </summary>
    public static class RichMarkdown
    {
        public const int DefaultMaxProductTitleLength = 45;

        public static string EscapeTableCell(string value)
        {
            // Telegram parses GFM-like Markdown tables in rich messages.
            // Escape pipes so cell content cannot break the table structure.
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        public static string EscapeLinkText(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        public static string TruncateProductTitle(string value, int maxLength = DefaultMaxProductTitleLength)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text[..(maxLength - 1)].TrimEnd() + "…";
        }

        public static string FormatProductLink(string productName, string productUrl, int maxTitleLength = DefaultMaxProductTitleLength)
        {
            var title = EscapeLinkText(TruncateProductTitle(productName, maxTitleLength));
            return $"[{title}]({productUrl})";
        }
    }
}
