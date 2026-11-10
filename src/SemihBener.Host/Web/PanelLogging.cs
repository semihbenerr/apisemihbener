using System.Collections.Concurrent;

namespace SemihBener.Host.Web;

public sealed record LogEntry(DateTime TimestampUtc, LogLevel Level, string Category, string Message, string? Exception);

/// <summary>
/// Uyarı ve hataları panelde göstermek için bellekte (son 1000 kayıt) ve günlük dosyalarda
/// (App_Data/logs/yyyy-MM-dd.log) tutar.
/// </summary>
public sealed class InMemoryLogStore(string logDirectory)
{
    private const int Capacity = 1000;
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly object _fileLock = new();

    public IReadOnlyList<LogEntry> Snapshot() => _entries.Reverse().ToList();

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }

        try
        {
            var line = $"{entry.TimestampUtc:O} [{entry.Level}] {entry.Category}: {entry.Message}";
            if (entry.Exception is not null)
            {
                line += Environment.NewLine + entry.Exception;
            }

            lock (_fileLock)
            {
                File.AppendAllText(
                    Path.Combine(logDirectory, $"{entry.TimestampUtc:yyyy-MM-dd}.log"),
                    line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Log yazılamaması uygulamayı durdurmamalı.
        }
    }
}

public sealed class PanelLoggerProvider(InMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new PanelLogger(categoryName, store);

    public void Dispose()
    {
    }

    private sealed class PanelLogger(string category, InMemoryLogStore store) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            store.Add(new LogEntry(DateTime.UtcNow, logLevel, category, formatter(state, exception), exception?.ToString()));
        }
    }
}
