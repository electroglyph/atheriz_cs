using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core;

public static class AtherizLogger
{
    public const string DefaultCategory = "atheriz";
    private const string Formatter = "{Level}: {Category}: {Message}";
    private static readonly Lock _lock = new();
    // Dedicated file lock (F009): AppendToFile does check+rotate+append; without a lock
    // concurrent ticks interleave lines and can corrupt server.log. Kept separate from
    // _lock so file IO never blocks logger-factory access.
    private static readonly Lock _fileLock = new();
    private static ILoggerFactory? _factory;
    // True when the live factory is ours (sink provider owns echo + file):
    // a foreign factory from Configure() only records, so Write() still
    // echoes + appends after logging there.
    private static bool _ownsFactory;
    // Volatile: read lock-free on the logging hot path, written under
    // _lock in ApplySettings — a plain field allows indefinite stale
    // filtering on weak-memory hardware.
    private static volatile LogLevel _level = LogLevel.Information;
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
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxFiles = 5;

    static AtherizLogger()
    {
        ApplySettings();
        SetupLogger();
    }

    // Atomic pair snapshot: path and level publish together, so a
    // reader observes one generation exactly — never a mixed pair.
    internal static (string SavePath, LogLevel Level) SnapshotSettings()
    {
        lock (_lock) return (_savePath, _level);
    }

    // LEVEL CONTRACT: debug/info/warning/error/critical map case-insensitively; anything else
    // falls back to Information here, but AtherizSettingsValidator rejects unknown LogLevel
    // strings at config load — so an unknown level can only arrive via direct assignment.
    public static void ApplySettings(AtherizSettings? settings = null)
    {
        var s = settings ?? AtherizSettings.Global;
        // Publish path and level in ONE hold: the old split holds let a
        // concurrent ApplySettings interleave a mixed path/level generation.
        // The factory refresh stays outside (SetupLogger takes the
        // non-reentrant _lock itself — holding it across would self-deadlock).
        var path = s.SavePath ?? "save";
        var level = LevelMap.TryGetValue(s.LogLevel ?? "info", out var lv) ? lv : LogLevel.Information;
        lock (_lock)
        {
            _savePath = path;
            _level = level;
            // Factory-refresh: the console provider (and minimum level) freeze at first
            // construction, so a changed level rebuilds the factory instead of silently
            // sticking. Write() also re-checks _level per call, so in-flight writers stay correct.
            if (_factory is not null && _appliedLevel != _level)
            {
                try { _factory.Dispose(); } catch { }
                _factory = null;
            }
        }
        if (_factory is null) SetupLogger();
        lock (_lock) { _appliedLevel = _level; }
    }

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
                // Single-echo: Write() logs here exactly once. The sink owns
                // echo + file (which CaptureAtherizLog routes via
                // Console.Error), so no provider here would print twice and
                // zero providers would disable IsEnabled entirely.
                b.AddProvider(new AtherizSinkProvider());
            });
            _ownsFactory = true;
        }
        catch
        {
            _factory = null;
        }
    }

    public static void Configure(ILoggerFactory factory)
    {
        lock (_lock) { _factory = factory; _ownsFactory = false; }
    }

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

    // The factory's sink: level-gated echo + file append in one place, so
    // Write() below is a single Log() call instead of log-then-echo.
    private sealed class AtherizSinkProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName);
        public void Dispose() { }
        private sealed class SinkLogger : ILogger
        {
            private readonly string _cat;
            public SinkLogger(string cat) => _cat = cat;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _level;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                EchoAndAppend(logLevel, _cat, formatter(state, exception), exception);
            }
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

    private static string? _createdDir; // best-effort cache: last directory known created

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
            // Single encode: this byte array feeds the rotation size check
            // and the write below (no separate GetByteCount pass).
            var payload = System.Text.Encoding.UTF8.GetBytes(
                FormatLine(level, category, message, ex, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} ") + Environment.NewLine);
            if (_createdDir != dir)
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            // size check + rotate before append
            try
            {
                var info = new FileInfo(file);
                if (info.Exists && info.Length + payload.Length > MaxFileBytes)
                    RotateLocked(file);
            }
            catch { }
            try
            {
                using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(payload, 0, payload.Length);
                _createdDir = dir;
                Volatile.Write(ref _lastFileFailureTicks, 0);
            }
            catch
            {
                // A failed write may mean a directory deleted at runtime:
                // forget the cache so the next call recreates it (a healed
                // directory writes on the very next call, no cooldown skip).
                _createdDir = null;
                Volatile.Write(ref _lastFileFailureTicks, DateTime.UtcNow.Ticks);
            }
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
        // logger.py, where a below-level debug never reaches any handler.)
        if (level < _level) return;
        ILogger? logger = null;
        bool own;
        // Per-category logger: the sink stamps the creation category on
        // every line, so routing through one default-category logger would
        // mute the caller's category (e.g. Node context lines). The factory
        // caches logger instances per name, so this is a lookup, not a build.
        lock (_lock) { own = _ownsFactory; if (_factory is not null) logger = _factory.CreateLogger(category); }
        if (logger is not null)
        {
            try
            {
                logger.Log(level, 0, message, ex, (s, e) => e is not null ? $"{s}\n{e}" : s);
                // Our sink already echoed + appended; a foreign factory only
                // records, so echo + append for that path below.
                if (own) return;
            }
            catch { }
        }
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
            else if (level == LogLevel.Warning) LogWarning(message);
            else LogInformation(message);
        }
        catch { try { Console.Error.WriteLine(message); } catch { } }
    }
    public static void LogErrorRobust(string message) => LogRobust(LogLevel.Error, message);
    public static void LogInformationRobust(string message) => LogRobust(LogLevel.Information, message);

    // Compat overloads mirroring ILogger
    public static void Info(string msg) => LogInformation(msg);
    public static void Warning(string msg) => LogWarning(msg);
    public static void Error(string msg) => LogError(msg);
}
