using Atheriz.Core;
using Microsoft.Extensions.Logging;

namespace Atheriz.Server.Infrastructure;

// Port of atheriz/logger.py:43 file handling + RotatingFileHandler 5M*5 for save/server.log
// Minimal FileLogger for Server host — honors AtherizSettings.SavePath and LOG_LEVEL
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _savePath;
    private readonly LogLevel _minLevel;
    private readonly object _lock = new();
    // F009: no permanent _disabled latch. A single transient IO error (locked file, full
    // disk that later frees) used to silence file logging for the rest of the process.
    // Failures are counted for diagnostics and every write retries.
    private long _writeFailures;

    public FileLoggerProvider(string savePath, LogLevel minLevel = LogLevel.Information)
    {
        _savePath = savePath ?? "save";
        _minLevel = minLevel;
        try { Directory.CreateDirectory(_savePath); } catch { }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    internal bool IsEnabled(LogLevel level) => level >= _minLevel;

    internal long WriteFailures => Interlocked.Read(ref _writeFailures);

    internal void WriteLog(LogLevel level, string category, string message, Exception? ex)
    {
        if (!IsEnabled(level)) return;
        try
        {
            var file = Path.Combine(_savePath, "server.log");
            try { Directory.CreateDirectory(_savePath); } catch { }
            var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {level.ToString().ToUpperInvariant()}: {category}: {message}";
            if (ex != null) line += $"\n{ex}";
            line += Environment.NewLine;
            lock (_lock)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        var len = new FileInfo(file).Length;
                        if (len + line.Length > AtherizLogger.MaxFileBytes) Rotate(file);
                    }
                }
                catch { }
                try { File.AppendAllText(file, line); }
                catch { Interlocked.Increment(ref _writeFailures); }
            }
        }
        catch { Interlocked.Increment(ref _writeFailures); }
    }

    private void Rotate(string file) => AtherizLogger.Rotate(file);

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;
        public FileLogger(FileLoggerProvider provider, string category) { _provider = provider; _category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            _provider.WriteLog(logLevel, _category, msg, exception);
            // also echo to fallback via Console.Error? Already via AddConsole
        }
    }
}

public static class FileLoggerExtensions
{
    // Port of logger.py:43 AddFile/save/server.log 5M*5 equivalent — use AddProvider
    public static ILoggingBuilder AddAtherizFileLogger(this ILoggingBuilder builder, string savePath, LogLevel minLevel = LogLevel.Information)
    {
        builder.AddProvider(new FileLoggerProvider(savePath, minLevel));
        return builder;
    }
}
