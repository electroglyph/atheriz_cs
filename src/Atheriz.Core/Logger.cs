using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core;

// Port of atheriz/logger.py:12 shared logger
public static class AtherizLogger
{
    // Port of logger.py:16 logger = getLogger("atheriz")
    public const string DefaultCategory = "atheriz";
    // Port of logger.py:17 FORMATTER = "%(levelname)s: %(name)s: %(message)s"
    private const string Formatter = "{Level}: {Category}: {Message}";
    private static readonly Lock _lock = new();
    // Dedicated file lock (F009): AppendToFile does check+rotate+append; without a lock
    // concurrent ticks interleave lines and can corrupt server.log. Kept separate from
    // _lock so file IO never blocks logger-factory access.
    private static readonly Lock _fileLock = new();
    private static ILoggerFactory? _factory;
    private static ILogger? _cachedDefault;
    private static LogLevel _level = LogLevel.Information; // Port of logger.py:28 default info
    // Port of logger.py:21 level_map debug/info/warning/error/critical.
    // Built once (frozen, case-insensitive): ApplySettings runs on settings
    // change, but there is no reason to allocate the 5-entry map per call.
    // The lock takes in ApplySettings stay split (four separate holds):
    // coalescing them around SetupLogger would self-deadlock on the
    // non-reentrant lock.
    private static readonly FrozenDictionary<string, LogLevel> LevelMap =
        new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase)
        {
            ["debug"] = LogLevel.Debug,
            ["info"] = LogLevel.Information,
            ["warning"] = LogLevel.Warning,
            ["error"] = LogLevel.Error,
            ["critical"] = LogLevel.Critical,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    private static LogLevel _appliedLevel = LogLevel.Information; // minimum level the live factory was built with
    // Volatile: published under _lock in ApplySettings but read under the
    // separate _fileLock in AppendToFile, so the lock alone gives readers
    // no visibility edge; volatility covers the cross-lock publish.
    private static volatile string _savePath = "save";
    // the write-only latch is gone — every write attempts
    // the append (a transient failure never mutes later writes). Last failure
    // ticks are recorded lock-free for backoff/diagnostics; a healed directory
    // writes on the very next call (no cooldown skip).
    private static long _lastFileFailureTicks;
    public const long MaxFileBytes = 5 * 1024 * 1024; // Port of RotatingFileHandler 5M
    public const int MaxFiles = 5;

    static AtherizLogger()
    {
        // Port of logger.py:42 apply_settings() + _setup_logger()
        ApplySettings();
        SetupLogger();
    }

    // Port of logger.py:19 apply_settings
    // LEVEL CONTRACT: debug/info/warning/error/critical map case-insensitively; anything else
    // falls back to Information here, but AtherizSettingsValidator rejects unknown LogLevel
    // strings at config load — so an unknown level can only arrive via direct assignment.
    public static void ApplySettings(AtherizSettings? settings = null)
    {
        var s = settings ?? AtherizSettings.Global;
        // Published with the level/factory state under one hold so a
        // concurrent ApplySettings cannot interleave path and level.
        lock (_lock) { _savePath = s.SavePath ?? "save"; }
        // Port of logger.py:21 level_map debug/info/warning/error/critical
        lock (_lock)
        {
            _level = LevelMap.TryGetValue(s.LogLevel ?? "info", out var lv) ? lv : LogLevel.Information;
            // Factory-refresh: the console provider (and minimum level) freeze at first
            // construction, so a changed level rebuilds the factory instead of silently
            // sticking. Write() also re-checks _level per call, so in-flight writers stay correct.
            if (_factory is not null && _appliedLevel != _level)
            {
                try { _factory.Dispose(); } catch { }
                _factory = null;
                _cachedDefault = null;
            }
        }
        if (_factory is null) SetupLogger();
        lock (_lock) { _appliedLevel = _level; }
    }

    // Port of logger.py:31 _setup_logger
    private static void SetupLogger()
    {
        lock (_lock) SetupLoggerLocked();
    }

    // Runs with _lock already held (GetLogger holds it; SetupLogger takes it
    // via the wrapper above). Split out so GetLogger's setup path does not
    // nest a second take of the non-reentrant lock.
    private static void SetupLoggerLocked()
    {
        if (_factory is not null) return;
        try
        {
            _factory = LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(_level);
                // Single-echo: no console provider here. Write() already echoes every
                // kept message to Console.Error (which CaptureAtherizLog routes) and
                // appends to save/server.log — a provider would print each line twice.
                // A sink provider is still required: with zero providers every
                // ILogger.IsEnabled returns false regardless of minimum level.
                // NullLoggerProvider honors the factory minimum level but drops all
                // records (Write() owns echo + file).
                b.AddProvider(new NullLoggerProvider(_level));
            });
            _cachedDefault = _factory.CreateLogger(DefaultCategory);
        }
        catch
        {
            _factory = null;
            _cachedDefault = null;
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
            if (_factory is not null) return _factory.CreateLogger(category);
            // fallback to default factory if not configured
            SetupLoggerLocked();
            if (_factory is not null) return _factory.CreateLogger(category);
            return new FallbackLogger(category);
        }
    }

    private sealed class NullLoggerProvider : ILoggerProvider
    {
        private readonly LogLevel _min;
        public NullLoggerProvider(LogLevel min) => _min = min;
        public ILogger CreateLogger(string categoryName) => new NullLogger(_min);
        public void Dispose() { }
        private sealed class NullLogger : ILogger
        {
            private readonly LogLevel _min;
            public NullLogger(LogLevel min) => _min = min;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _min;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
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
            var line = FormatLine(logLevel, _cat, msg, exception);
            try { Console.Error.WriteLine(line); } catch { }
            try { AppendToFile(logLevel, _cat, msg, exception); } catch { }
        }
    }

    private static void AppendToFile(LogLevel level, string category, string message, Exception? ex)
    {
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
            var line = FormatLine(level, category, message, ex, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} ");
            line += Environment.NewLine;
            // size check + rotate before append
            try
            {
                if (File.Exists(file))
                {
                    var info = new FileInfo(file);
                    if (info.Length + System.Text.Encoding.UTF8.GetByteCount(line) > MaxFileBytes)
                        RotateLocked(file);
                }
            }
            catch { }
            try { File.AppendAllText(file, line); Volatile.Write(ref _lastFileFailureTicks, 0); }
            catch { Volatile.Write(ref _lastFileFailureTicks, DateTime.UtcNow.Ticks); }
        }
        catch { }
        }
    }

    public static void Rotate(string file)
    {
        // F009: same file lock as AppendToFile — rotation never races
        // appends. AppendToFile calls RotateLocked (no second take), so the
        // non-reentrant lock never nests.
        lock (_fileLock) RotateLocked(file);
    }

    // Runs with _fileLock already held (Rotate takes it via the wrapper).
    private static void RotateLocked(string file)
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

    private static string FormatLine(LogLevel level, string category, string message, Exception? ex, string? timestamp = null)
    {
        var line = $"{timestamp}{level.ToString().ToUpperInvariant()}: {category}: {message}";
        if (ex is not null) line += $"\n{ex}";
        return line;
    }

    private static void Write(LogLevel level, string category, string message, Exception? ex = null)
    {
        // filtered levels are dropped — operators silencing via
        // LogLevel must not pay debug IO on every call. (Port of
        // logger.py, where a below-level debug never reaches any handler.)
        if (level < _level) return;
        ILogger? logger = null;
        lock (_lock) logger = _cachedDefault;
        if (logger is not null)
        {
            try
            {
                logger.Log(level, 0, message, ex, (s, e) => e is not null ? $"{s}\n{e}" : s);
                // Also echo to Console.Error for CaptureAtherizLog routing (throttling tests rely on Console.Error capture)
                EchoAndAppend(level, category, message, ex);
                return;
            }
            catch { }
        }
        // Fallback Console.Error — Port of logger.py:37 StreamHandler
        EchoAndAppend(level, category, message, ex);
    }

    // Shared echo-and-append tail for both Write branches above: format once,
    // echo to Console.Error, append to the file. Per-sink bytes are identical
    // in both branches (the logger branch additionally records via ILogger).
    private static void EchoAndAppend(LogLevel level, string category, string message, Exception? ex)
    {
        var line = FormatLine(level, category, message, ex);
        try { Console.Error.WriteLine(line); } catch { }
        try { AppendToFile(level, category, message, ex); } catch { }
    }

    public static void LogInformation(string message, string category = DefaultCategory) => Write(LogLevel.Information, category, message);
    public static void LogWarning(string message, string category = DefaultCategory) => Write(LogLevel.Warning, category, message);
    public static void LogError(string message, string category = DefaultCategory) => Write(LogLevel.Error, category, message);
    public static void LogError(string message, Exception ex, string category = DefaultCategory) => Write(LogLevel.Error, category, message, ex);
    public static void LogDebug(string message, string category = DefaultCategory) => Write(LogLevel.Debug, category, message);
    public static void LogCritical(string message, string category = DefaultCategory) => Write(LogLevel.Critical, category, message);

    // Tree-wide fallback idiom for save/shutdown paths: logging must never
    // throw out of these. One body (LogRobust); the two historical names stay
    // as one-line forwards — external game code calls them.
    public static void LogRobust(LogLevel level, string message)
    {
        try
        {
            if (level == LogLevel.Error) LogError(message);
            else LogInformation(message);
        }
        catch { try { Console.Error.WriteLine(message); } catch { } }
    }
    public static void LogErrorRobust(string message)
    {
        try { LogRobust(LogLevel.Error, message); } catch { Console.Error.WriteLine(message); }
    }
    public static void LogInformationRobust(string message)
    {
        try { LogRobust(LogLevel.Information, message); } catch { Console.Error.WriteLine(message); }
    }

    // Compat overloads mirroring ILogger
    public static void Info(string msg) => LogInformation(msg);
    public static void Warning(string msg) => LogWarning(msg);
    public static void Error(string msg) => LogError(msg);
}
