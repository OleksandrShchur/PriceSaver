using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// Lightweight <see cref="ILogger{T}"/> that captures log entries so tests
    /// can assert that warnings/errors were emitted without mocking the
    /// non-virtual logging extension methods.
    /// </summary>
    public sealed class TestLogger<T> : ILogger<T>
    {
        public record Entry(LogLevel Level, string Message, Exception? Exception);

        public ConcurrentBag<Entry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }

        public bool HasLevel(LogLevel level) => Entries.Any(e => e.Level == level);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
