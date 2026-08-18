using PriceSaver.Server.Helpers;

namespace PriceSaver.Server.Tests.Helpers
{
    public class LogFileReaderTests
    {
        [Fact]
        public async Task OpenSharedCopyAsync_ReadsFileHeldOpenForWrite()
        {
            var path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "hello-log");
                await using var writer = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.Read);

                if (OperatingSystem.IsWindows())
                {
                    var openRead = () => File.OpenRead(path);
                    openRead.Should().Throw<IOException>();
                }

                await using var copy = await LogFileReader.OpenSharedCopyAsync(path);
                using var reader = new StreamReader(copy, leaveOpen: true);
                var text = await reader.ReadToEndAsync();
                text.Should().Be("hello-log");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
