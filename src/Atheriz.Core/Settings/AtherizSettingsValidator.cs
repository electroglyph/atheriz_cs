using Microsoft.Extensions.Options;

namespace Atheriz.Core.Settings;

/// <summary>
/// Validates <see cref="AtherizSettings"/> on startup via <see cref="IValidateOptions{TOptions}"/>.
/// Ports the implicit invariants from <c>atheriz/settings.py</c> that were previously unchecked.
/// </summary>
public sealed class AtherizSettingsValidator : IValidateOptions<AtherizSettings>
{
    // B-UTL-5: valid vocabulary per Logger.cs:47-50 (unknown levels silently fall back
    // to Information there, so reject them here).
    private static readonly HashSet<string> ValidLogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "debug", "info", "warning", "error", "critical",
    };

    public ValidateOptionsResult Validate(string? name, AtherizSettings options)
    {
        var failures = new List<string>();

        if (options.MaxCharacters <= 0 || options.MaxCharacters > 100)
            failures.Add($"MaxCharacters must be >0 and <=100 (was {options.MaxCharacters}).");
        // Python settings.py:83 MAX_CHARACTERS=5; the <=100 upper bound stays permissive by design.
        if (options.MaxConnectionsPerIp < 0)
            failures.Add($"MaxConnectionsPerIp must be >=0 (0 = unlimited, was {options.MaxConnectionsPerIp}).");
        if (options.WebsocketMaxMessageSize <= 0)
            failures.Add($"WebsocketMaxMessageSize must be >0 (was {options.WebsocketMaxMessageSize}).");
        if (options.WebsocketMaxPendingSends <= 0)
            failures.Add($"WebsocketMaxPendingSends must be >0 (was {options.WebsocketMaxPendingSends}).");
        if (options.WebsocketMaxPendingBytes <= 0)
            failures.Add($"WebsocketMaxPendingBytes must be >0 (was {options.WebsocketMaxPendingBytes}).");
        if (options.TelnetMaxPendingBytes <= 0)
            failures.Add($"TelnetMaxPendingBytes must be >0 (was {options.TelnetMaxPendingBytes}).");
        if (options.ConnectionInputQueueLimit <= 0)
            failures.Add($"ConnectionInputQueueLimit must be >0 (was {options.ConnectionInputQueueLimit}).");
        if (options.ThreadpoolQueueLimit <= 0)
            failures.Add($"ThreadpoolQueueLimit must be >0 (was {options.ThreadpoolQueueLimit}).");
        if (options.ThreadpoolLimit is < 1)
            failures.Add($"ThreadpoolLimit must be null or >=1 (was {options.ThreadpoolLimit}).");
        if (options.ThreadpoolReliefLimit is < 1)
            failures.Add($"ThreadpoolReliefLimit must be null or >=1 (was {options.ThreadpoolReliefLimit}).");
        if (options.ThreadpoolWatchdogSeconds <= 0)
            failures.Add($"ThreadpoolWatchdogSeconds must be >0 (was {options.ThreadpoolWatchdogSeconds}).");
        if (options.ThreadpoolWatchdogInterval <= 0)
            failures.Add($"ThreadpoolWatchdogInterval must be >0 (was {options.ThreadpoolWatchdogInterval}).");
        if (options.FuncparserMaxNesting <= 0)
            failures.Add($"FuncparserMaxNesting must be >0 (was {options.FuncparserMaxNesting}).");
        if (options.MapeditMaxChains <= 0)
            failures.Add($"MapeditMaxChains must be >0 (was {options.MapeditMaxChains}).");
        if (options.MenuPromptTimeout <= 0)
            failures.Add($"MenuPromptTimeout must be >0 (was {options.MenuPromptTimeout}).");
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(options.SavePath); }
        catch (Exception ex) { failures.Add($"SavePath invalid: {ex.Message}"); }
        try { Atheriz.Core.Utils.PathGuards.GuardSecretPath(options.SecretPath); }
        catch (Exception ex) { failures.Add($"SecretPath invalid: {ex.Message}"); }
        if (options.ChannelHistoryLimit <= 0)
            failures.Add($"ChannelHistoryLimit must be >0 (was {options.ChannelHistoryLimit}).");

        if (options.MaxAccountNameLength < 3)
            failures.Add($"MaxAccountNameLength must be >=3 (was {options.MaxAccountNameLength}).");
        if (options.MaxCharacterNameLength < 3)
            failures.Add($"MaxCharacterNameLength must be >=3 (was {options.MaxCharacterNameLength}).");
        if (options.MinPasswordLength < 1)
            failures.Add($"MinPasswordLength must be >=1 (was {options.MinPasswordLength}).");
        if (options.MaxPasswordLength < options.MinPasswordLength)
            failures.Add($"MaxPasswordLength ({options.MaxPasswordLength}) must be >= MinPasswordLength ({options.MinPasswordLength}).");

        if (options.WebserverPort < 1024 || options.WebserverPort > 65535)
            failures.Add($"WebserverPort must be 1024-65535 (was {options.WebserverPort}).");
        // Mirror the Kestrel/telnet fail-fast binds: "::" is the IPv6-any
        // spelling, anything else must parse as an IP literal.
        if (!string.Equals(options.WebserverInterface, "::", StringComparison.Ordinal)
            && !System.Net.IPAddress.TryParse(options.WebserverInterface ?? "", out _))
            failures.Add($"WebserverInterface unparseable: '{options.WebserverInterface}'.");
        if (!string.Equals(options.TelnetInterface, "::", StringComparison.Ordinal)
            && !System.Net.IPAddress.TryParse(options.TelnetInterface ?? "", out _))
            failures.Add($"TelnetInterface unparseable: '{options.TelnetInterface}'.");
        if (options.WebserverPort == options.TelnetPort)
            failures.Add($"WebserverPort ({options.WebserverPort}) must differ from TelnetPort ({options.TelnetPort}).");
        if (string.IsNullOrWhiteSpace(options.ServerName))
            failures.Add("ServerName must be non-empty.");
        if (options.MinutesPerHour <= 0)
            failures.Add($"MinutesPerHour must be >0 (was {options.MinutesPerHour}).");
        if (options.HoursPerDay <= 0)
            failures.Add($"HoursPerDay must be >0 (was {options.HoursPerDay}).");
        if (options.DaysPerMonth <= 0)
            failures.Add($"DaysPerMonth must be >0 (was {options.DaysPerMonth}).");
        if (options.MonthsPerYear <= 0)
            failures.Add($"MonthsPerYear must be >0 (was {options.MonthsPerYear}).");
        // Fail closed on TLS: a configured cert must exist and load. A missing file
        // is always a config error; a present-but-unloadable one is tolerated only
        // under an explicit insecure-fallback opt-in (Kestrel warns at startup).
        if (!string.IsNullOrEmpty(options.SslCertFile))
        {
            if (!File.Exists(options.SslCertFile))
                failures.Add($"SslCertFile not found: {options.SslCertFile}");
            else
            {
                if (!string.IsNullOrEmpty(options.SslKeyFile) && !File.Exists(options.SslKeyFile))
                    failures.Add($"SslKeyFile not found: {options.SslKeyFile}");
                else if (!options.AllowInsecureTlsFallback)
                {
                    try { using var cert = Atheriz.Core.Utils.TlsCertLoader.Load(options.SslCertFile, options.SslKeyFile); }
                    catch (Exception ex) { failures.Add($"SslCertFile unloadable: {options.SslCertFile} ({ex.Message})"); }
                }
            }
        }
        else if (!string.IsNullOrEmpty(options.SslKeyFile) && !File.Exists(options.SslKeyFile))
            failures.Add($"SslKeyFile not found: {options.SslKeyFile}");
        if (options.TelnetEnabled)
        {
            if (options.TelnetPort < 1024 || options.TelnetPort > 65535)
                failures.Add($"TelnetPort must be 1024-65535 when TelnetEnabled (was {options.TelnetPort}).");
        }

        if (options.TelnetNawsMaxCols <= 0)
            failures.Add($"TelnetNawsMaxCols must be >0 (was {options.TelnetNawsMaxCols}).");
        if (options.TelnetNawsMaxRows <= 0)
            failures.Add($"TelnetNawsMaxRows must be >0 (was {options.TelnetNawsMaxRows}).");
        if (options.TelnetNawsMinCols <= 0)
            failures.Add($"TelnetNawsMinCols must be >0 (was {options.TelnetNawsMinCols}).");
        if (options.TelnetNawsMinRows <= 0)
            failures.Add($"TelnetNawsMinRows must be >0 (was {options.TelnetNawsMinRows}).");
        if (options.TelnetNawsMaxCols < options.TelnetNawsMinCols)
            failures.Add($"TelnetNawsMaxCols ({options.TelnetNawsMaxCols}) must be >= TelnetNawsMinCols ({options.TelnetNawsMinCols}).");
        if (options.TelnetNawsMaxRows < options.TelnetNawsMinRows)
            failures.Add($"TelnetNawsMaxRows ({options.TelnetNawsMaxRows}) must be >= TelnetNawsMinRows ({options.TelnetNawsMinRows}).");

        if (options.TelnetConnectionTimeout <= 0)
            failures.Add($"TelnetConnectionTimeout must be >0 (was {options.TelnetConnectionTimeout}).");
        if (options.TelnetMaxLine <= 0)
            failures.Add($"TelnetMaxLine must be >0 (was {options.TelnetMaxLine}).");
        if (options.MaxSearchDepth <= 0)
            failures.Add($"MaxSearchDepth must be >0 (was {options.MaxSearchDepth}).");
        if (options.MaxAstarIterations <= 0)
            failures.Add($"MaxAstarIterations must be >0 (was {options.MaxAstarIterations}).");
        if (string.IsNullOrWhiteSpace(options.LogLevel) || !ValidLogLevels.Contains(options.LogLevel.Trim()))
            failures.Add($"LogLevel must be one of debug/info/warning/error/critical (was '{options.LogLevel}').");
        if (options.SecondsPerMinute <= 0)
            failures.Add($"SecondsPerMinute must be >0 (was {options.SecondsPerMinute}).");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(string.Join("; ", failures));
    }
}
