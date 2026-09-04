namespace Atheriz.Core.Settings;

/// <summary>
/// Strongly-typed mirror of <c>atheriz/settings.py</c>.
/// Bindable via IOptionsMonitor&lt;AtherizSettings&gt; from appsettings.json + env.
/// Mutable at runtime (hot-reload) — setters intentional.
/// </summary>
public sealed class AtherizSettings
{
    private static readonly object _globalLock = new();
    private static AtherizSettings _global = new();
    public static AtherizSettings Global
    {
        get { lock (_globalLock) return _global; }
        set { lock (_globalLock) _global = value ?? new AtherizSettings(); }
    }

    /// <summary>Shared default instance to avoid per-call <c>new AtherizSettings()</c> allocations.</summary>
    public static AtherizSettings Default { get; } = new();
    // Paths
    public string SavePath { get; set; } = "save";
    public string SecretPath { get; set; } = "secret";
    public string ServerName { get; set; } = "AtheriZ";
    public string ServerHostname { get; set; } = "localhost";

    // Network
    public bool WebsocketEnabled { get; set; } = true;
    public int WebsocketMaxMessageSize { get; set; } = 65536;
    public int WebsocketMaxPendingSends { get; set; } = 256;
    public int WebsocketMaxPendingBytes { get; set; } = 4 * 1024 * 1024;
    public int TelnetMaxPendingBytes { get; set; } = 1 * 1024 * 1024;
    public bool TelnetEnabled { get; set; } = true;
    public int TelnetPort { get; set; } = 4444;
    public string TelnetInterface { get; set; } = "0.0.0.0";
    public bool TelnetTlsEnabled { get; set; } = false;
    public int TelnetConnectionTimeout { get; set; } = 300;
    public int TelnetNawsMinCols { get; set; } = 20;
    public int TelnetNawsMaxCols { get; set; } = 1000;
    public int TelnetNawsMinRows { get; set; } = 5;
    public int TelnetNawsMaxRows { get; set; } = 200;
    public int TelnetMaxLine { get; set; } = 65536; // port of telnet.py:388 getattr(settings,"TELNET_MAX_LINE",65536)
    public bool StripInputEscapeSequences { get; set; } = true;
    public int TermSizeMaxWidth { get; set; } = 1000;
    public int TermSizeMaxHeight { get; set; } = 1000;
    public int MapSizeMaxWidth { get; set; } = 1000;
    public int MapSizeMaxHeight { get; set; } = 1000;
    public string[] NetworkProtocols { get; set; } =
    [
        "Atheriz.Core.Network.WebSocketProtocol",
        "Atheriz.Core.Network.TelnetProtocol",
    ];

    public bool AccountCreationEnabled { get; set; } = true;
    public bool CharCreationEnabled { get; set; } = true;

    public bool WebserverEnabled { get; set; } = true;
    public int WebserverPort { get; set; } = 9999;
    public string WebserverInterface { get; set; } = "0.0.0.0";
    public string? SslCertFile { get; set; } = Environment.GetEnvironmentVariable("ATHERIZ_SSL_CERTFILE");
    public string? SslKeyFile { get; set; } = Environment.GetEnvironmentVariable("ATHERIZ_SSL_KEYFILE");
    /// <summary>
    /// When true (default, faithful to the Python server which always starts), a configured
    /// but unloadable TLS certificate logs a warning and serves plaintext. Set false to
    /// fail fast instead of serving the admin token over plaintext unnoticed.
    /// </summary>
    public bool AllowInsecureTlsFallback { get; set; } = true;
    public bool WebclientSyncCheck { get; set; } = true;

    public int? ThreadpoolLimit { get; set; } = Environment.ProcessorCount;
    public int? ThreadpoolReliefLimit { get; set; } = Environment.ProcessorCount;
    public double ThreadpoolWatchdogSeconds { get; set; } = 30.0;
    public double ThreadpoolWatchdogInterval { get; set; } = 5.0;
    public int ThreadpoolQueueLimit { get; set; } = 10000;
    public int ConnectionInputQueueLimit { get; set; } = 100;

    public int MaxCharacters { get; set; } = 5;
    public double DefaultTickSeconds { get; set; } = 1.0;
    public double DefaultEnclosedSoundAttenuation { get; set; } = 20.0;
    public double DefaultOpenSoundAttenuation { get; set; } = 10.0;
    public double DefaultAmbientSoundLevel { get; set; } = 5.0;

    public bool GuestEnabled { get; set; } = true;
    public string FuncparserStartChar { get; set; } = "$";
    public string FuncparserEscapeChar { get; set; } = "\\";
    public int FuncparserMaxNesting { get; set; } = 20;
    public int MaxSearchDepth { get; set; } = 100;
    public int MaxAstarIterations { get; set; } = 50000;
    public int ClientDefaultWidth { get; set; } = 78;
    public int ClientDefaultHeight { get; set; } = 45;
    public bool Debug { get; set; } = true;
    public string LogLevel { get; set; } = "info";

    public bool SaveChannelHistory { get; set; } = true;
    public int ChannelHistoryLimit { get; set; } = 50;
    public int MaxLoginAttempts { get; set; } = 3;
    public int LoginAttemptCooldown { get; set; } = 100;
    public int MaxConnectionsPerIp { get; set; } = 2;
    public int MenuPromptTimeout { get; set; } = 60;
    public int CreationCooldown { get; set; } = 60;
    public int MapeditMaxChains { get; set; } = 256;
    public int MaxAccountNameLength { get; set; } = 20;
    public int MaxCharacterNameLength { get; set; } = 20;
    public int MinPasswordLength { get; set; } = 8;
    public int MaxPasswordLength { get; set; } = 1024;
    public bool AlwaysSaveAll { get; set; } = false;
    public Coord DefaultHome { get; set; } = new("limbo", 4, 4, 4);
    public bool MapEnabled { get; set; } = true;
    public bool LegendEnabled { get; set; } = true;
    public int MapFpsLimit { get; set; } = 5;
    public int MaxObjectsPerLegend { get; set; } = 30;
    public bool AutosavePlayersOnDisconnect { get; set; } = true;
    public bool AutosaveOnShutdown { get; set; } = true;
    public bool AutosaveOnReload { get; set; } = true;
    public int AutosaveMinutes { get; set; } = 0;
    public bool AutoCommandAliasing { get; set; } = true;
    public string[] AutoAliasIgnoredKeys { get; set; } =
        ["save", "quit", "wander", "exit", "logout", "disconnect", "none"];
    public bool ThreadsafeGettersSetters { get; set; } = true;
    public string DefaultRoomOutline { get; set; } = "single";
    public string SingleWallPlaceholder { get; set; } = "༗";
    public string DoubleWallPlaceholder { get; set; } = "༁";
    public string RoundedWallPlaceholder { get; set; } = "⍮";
    public string RoomPlaceholder { get; set; } = "℣";
    public string PathPlaceholder { get; set; } = "߶";
    public string RoadPlaceholder { get; set; } = "᭤";
    public string[] AllSymbols => [SingleWallPlaceholder, DoubleWallPlaceholder, RoundedWallPlaceholder, PathPlaceholder, RoadPlaceholder];

    // Door glyphs (ANSI) — exact defaults from settings.py
    public string NsClosedDoor { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m━\x1b[0m";
    public string NsOpenDoor1 { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m┚\x1b[0m";
    public string NsOpenDoor2 { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m┒\x1b[0m";
    public string EwClosedDoor { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m┃\x1b[0m";
    public string EwOpenDoor1 { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m┙\x1b[0m";
    public string EwOpenDoor2 { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m┕\x1b[0m";
    public string UdClosedDoor { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m╳\x1b[0m";
    public string UdOpenDoor { get; set; } = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m▽\x1b[0m";

    // Time
    public bool TimeSystemEnabled { get; set; } = true;
    public Func<Atheriz.Core.Objects.GameObject, bool> SolarReceiverLambda { get; set; } = x => x.IsPc && x.IsConnected;
    public Func<Atheriz.Core.Objects.GameObject, bool> LunarReceiverLambda { get; set; } = x => x.IsPc && x.IsConnected;
    public double TimeUpdateSeconds { get; set; } = 1.0;
    public int StartYear { get; set; } = 888;
    public double TickMinutes { get; set; } = 1.0;
    public int SecondsPerMinute { get; set; } = 60;
    public int MinutesPerHour { get; set; } = 60;
    public int HoursPerDay { get; set; } = 24;
    public int DaysPerMonth { get; set; } = 30;
    public int MonthsPerYear { get; set; } = 12;
    public int LunarCycleDays { get; set; } = 30;
    public int DaysPerWeek { get; set; } = 7;
    public int SunriseHour { get; set; } = 6;
    public int SunsetHour { get; set; } = 18;
    public string SunriseMessage { get; set; } = "The sun rises on a new day.";
    public string SunsetMessage { get; set; } = "The sun begins to set.";

    // Derived
    public int DaysPerYear => DaysPerMonth * MonthsPerYear;
    public int SecondsPerHour => SecondsPerMinute * MinutesPerHour;
    public int SecondsPerDay => SecondsPerHour * HoursPerDay;

    public (int Db, string Desc)[] LoudnessLevels { get; set; } =
    [
        (20, " nearly inaudible"),
        (40, " faint"),
        (60, ""),
        (80, " loud"),
        (100, " very loud"),
        (120, " extremely loud"),
    ];

    public (int Db, double Pct)[] ReplaceLevels { get; set; } =
    [
        (1, 95.0),
        (10, 80.0),
        (20, 60.0),
        (30, 40.0),
        (40, 20.0),
        (50, 10.0),
    ];

    // py sandbox
    public int PyMaxOutputLines { get; set; } = 200;
    public int PyMaxOutputBytes { get; set; } = 50_000;
    public int PyOutputFg { get; set; } = 15;
    public int KillPyCommandAfter { get; set; } = 5;
    public int PyMaxCodeBytes { get; set; } = 65_536;
    public int PyMaxAstNodes { get; set; } = 20_000;
    public int PyMaxLineEvents { get; set; } = 5_000_000;
    public bool PyRequireSuperuser { get; set; } = false;
}
