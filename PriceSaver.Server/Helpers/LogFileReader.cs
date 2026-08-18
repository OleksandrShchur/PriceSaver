namespace PriceSaver.Server.Helpers
{
    /// <summary>
    /// Reads log files that Serilog (or another writer) may still have open.
    /// </summary>
    public static class LogFileReader
    {
        public static async Task<MemoryStream> OpenSharedCopyAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
    }
}
