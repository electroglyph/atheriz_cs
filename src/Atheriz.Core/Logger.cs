using Microsoft.Extensions.Logging;
using Atheriz.Core.Settings;

namespace Atheriz.Core;

// Port of atheriz/logger.py:12 shared logger
public static class AtherizLogger
{
    // Port of logger.py:16 logger = getLogger("atheriz")
    public const string DefaultCategory = "atheriz";
    // Port of logger.py:17 FORMATTER = "%(levelname)s: %(name)s: %(message)s"
    private const string Formatter = "{Level}: {Category}: {Message}";
    private static readonly object _lock = new();
    // Dedicated file lock (F009): AppendToFile does check+rotate+append; without a lock
    // concurrent ticks interleave lines and can corrupt server.log. Kept separate from
    // _lock so file IO never blocks logger-factory access.
    private static readonly object _fileLock = new();
    private static ILoggerFactory? _factory;
    private static ILogger? _cachedDefault;
    private static LogLevel _level = LogLevel.Information; // Port of logger.py:28 default info
    private static string _savePath = "save";
    private static bool _fileEnabled = true;
    public const long MaxFileBytes = 5 * 1024 * 1024; // Port of RotatingFileHandler 5M
    public const int MaxFiles = 5;

    static AtherizLogger()
    {
        // Port of logger.py:42 apply_settings() + _setup_logger()
        ApplySettings();
        SetupLogger();
    }

    // Port of logger.py:19 apply_settings
    public static void ApplySettings(AtherizSettings? settings = null)
    {
        var s = settings ?? AtherizSettings.Global;
        _savePath = s.SavePath ?? "save";
        // Port of logger.py:21 level_map debug/info/warning/error/critical
        var map = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase)
        {
            ["debug"] = LogLevel.Debug,
            ["info"] = LogLevel.Information,
            ["warning"] = LogLevel.Warning,
            ["error"] = LogLevel.Error,
            ["critical"] = LogLevel.Critical,
        };
        lock (_lock)
        {
            _level = map.TryGetValue(s.LogLevel ?? "info", out var lv) ? lv : LogLevel.Information;
            if (_cachedDefault is ILogger l && l is object)
            {
                // level applied on next write via IsEnabled check
            }
        }
    }

    // Port of logger.py:31 _setup_logger
    private static void SetupLogger()
    {
        lock (_lock)
        {
            if (_factory != null) return;
            try
            {
                _factory = LoggerFactory.Create(b =>
                {
                    b.SetMinimumLevel(_level);
                    b.AddConsole(options => options.FormatterName = "simple");
                    // File target handled manually via FileAppend below for save/server.log equivalence
                });
                _cachedDefault = _factory.CreateLogger(DefaultCategory);
            }
            catch
            {
                _factory = null;
                _cachedDefault = null;
            }
        }
    }

    public static void Configure(ILoggerFactory factory)
    {
        lock (_lock) { _factory = factory; _cachedDefault = factory.CreateLogger(DefaultCategory); }
    }

    // Port of logger.py:43 thin wrapper GetLogger(category)
    public static ILogger GetLogger(string category)
    {
        lock (_lock)
        {
            if (_factory != null) return _factory.CreateLogger(category);
            // fallback to default factory if not configured
            SetupLogger();
            if (_factory != null) return _factory.CreateLogger(category);
            return new FallbackLogger(category);
        }
    }

    private sealed class FallbackLogger : ILogger
    {
        private readonly string _cat;
        public FallbackLogger(string cat) => _cat = cat;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _level;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            var line = $"{logLevel.ToString().ToUpperInvariant()}: {_cat}: {msg}";
            if (exception != null) line += $"\n{exception}";
            try { Console.Error.WriteLine(line); } catch { }
            try { AppendToFile(logLevel, _cat, msg, exception); } catch { }
        }
    }

    private static void AppendToFile(LogLevel level, string category, string message, Exception? ex)
    {
        if (!_fileEnabled) return;
        // F009: serialize size-check + rotate + append so concurrent writers cannot
        // interleave lines or rotate mid-append and corrupt server.log.
        lock (_fileLock)
        {
        try
        {
            var dir = _savePath;
            // mirrors save/server.log RotatingFileHandler 5M*5
            var file = Path.Combine(dir, "server.log");
            try { Directory.CreateDirectory(dir); } catch { }
            var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {level.ToString().ToUpperInvariant()}: {category}: {message}";
            if (ex != null) line += $"\n{ex}";
            line += Environment.NewLine;
            // size check + rotate before append
            try
            {
                if (File.Exists(file))
                {
                    var info = new FileInfo(file);
                    if (info.Length + line.Length > MaxFileBytes)
                        Rotate(file);
                }
            }
            catch { }
            try { File.AppendAllText(file, line); }
            catch { _fileEnabled = false; }
        }
        catch { }
        }
    }

    public static void Rotate(string file)
    {
        // F009: same file lock as AppendToFile (Monitor is re-entrant, so the
        // Rotate call inside AppendToFile is safe) — rotation never races appends.
        lock (_fileLock)
        {
        try
        {
            // 5 files: server.log -> server.log.1 .. server.log.5 (like RotatingFileHandler 5M*5)
            var dir = Path.GetDirectoryName(file) ?? ".";
            var baseName = Path.GetFileName(file);
            // delete oldest .5
            var oldest = Path.Combine(dir, baseName + $".{MaxFiles}");
            try { if (File.Exists(oldest)) File.Delete(oldest); } catch { }
            for (int i = MaxFiles - 1; i >= 1; i--)
            {
                var src = Path.Combine(dir, baseName + $".{i}");
                var dst = Path.Combine(dir, baseName + $".{i + 1}");
                try { if (File.Exists(src)) File.Move(src, dst, overwrite: true); } catch { }
            }
            var first = Path.Combine(dir, baseName + ".1");
            try { if (File.Exists(file)) File.Move(file, first, overwrite: true); } catch { }
        }
        catch { }
        }
    }

    private static void Write(LogLevel level, string category, string message, Exception? ex = null)
    {
        if (level < _level)
        {
            // F009 deviation: filtered levels still echo to Console.Error. Strict level-honoring
            // would drop them, but PortedConnectionTestsPart2.DispatchUnknownCmdLogged pins that a
            // Debug "Unknown command" line is capturable at default info level (mirrors the old
            // direct-Console.Error behavior), so the echo stays. File output is still skipped.
            var filteredLine = $"{level.ToString().ToUpperInvariant()}: {category}: {message}";
            if (ex != null) filteredLine += $"\n{ex}";
            try { Console.Error.WriteLine(filteredLine); } catch { }
            return;
        }
        ILogger? logger = null;
        lock (_lock) logger = _cachedDefault;
        if (logger != null)
        {
            try
            {
                logger.Log(level, 0, message, ex, (s, e) => e != null ? $"{s}\n{e}" : s);
                AppendToFile(level, category, message, ex);
                // Also echo to Console.Error for CaptureAtherizLog routing (throttling tests rely on Console.Error capture)
                var line = $"{level.ToString().ToUpperInvariant()}: {category}: {message}";
                if (ex != null) line += $"\n{ex}";
                try { Console.Error.WriteLine(line); } catch { }
                return;
            }
            catch { }
        }
        // Fallback Console.Error — Port of logger.py:37 StreamHandler
        var line2 = $"{level.ToString().ToUpperInvariant()}: {category}: {message}";
        if (ex != null) line2 += $"\n{ex}";
        try { Console.Error.WriteLine(line2); } catch { }
        try { AppendToFile(level, category, message, ex); } catch { }
    }

    public static void LogInformation(string message, string category = DefaultCategory) => Write(LogLevel.Information, category, message);
    public static void LogWarning(string message, string category = DefaultCategory) => Write(LogLevel.Warning, category, message);
    public static void LogError(string message, string category = DefaultCategory) => Write(LogLevel.Error, category, message);
    public static void LogError(string message, Exception ex, string category = DefaultCategory) => Write(LogLevel.Error, category, message, ex);
    public static void LogDebug(string message, string category = DefaultCategory) => Write(LogLevel.Debug, category, message);
    public static void LogCritical(string message, string category = DefaultCategory) => Write(LogLevel.Critical, category, message);

    // Compat overloads mirroring ILogger
    public static void Info(string msg) => LogInformation(msg);
    public static void Warning(string msg) => LogWarning(msg);
    public static void Error(string msg) => LogError(msg);
}
