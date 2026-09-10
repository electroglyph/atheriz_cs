using Microsoft.Extensions.Options;

namespace Atheriz.Core.Settings;

/// <summary>
/// Validates <see cref="AtherizSettings"/> on startup via <see cref="IValidateOptions{TOptions}"/>.
/// Ports the implicit invariants from <c>atheriz/settings.py</c> that were previously unchecked.
/// </summary>
public sealed class AtherizSettingsValidator : IValidateOptions<AtherizSettings>
{
    // valid vocabulary per Logger.cs:47-50 (unknown levels silently fall back
    // to Information there, so reject them here).
    private static readonly HashSet<string> ValidLogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "debug", "info", "warning", "error", "critical",
    };

    public ValidateOptionsResult Validate(string? name, AtherizSettings options)
    {
        List<string> failures = [];

        // Local failure collectors: messages are preformatted at the call site so
        // validation output cannot drift, and invocation order is unchanged so the
        // failure-list order stays stable.
        void FailWhen(bool invalid, string message)
        {
            if (invalid) failures.Add(message);
        }
        static bool IsValidInterface(string? value) =>
            string.Equals(value, "::", StringComparison.Ordinal)
            || System.Net.IPAddress.TryParse(value ?? "", out _);
        void CollectGuard(Action guard, Func<Exception, string> message)
        {
            try { guard(); }
            catch (Exception ex) { failures.Add(message(ex)); }
        }

        FailWhen(options.MaxCharacters <= 0 || options.MaxCharacters > 100,
            $"MaxCharacters must be >0 and <=100 (was {options.MaxCharacters}).");
        // Python settings.py:83 MAX_CHARACTERS=5; the <=100 upper bound stays permissive by design.
        FailWhen(options.MaxConnectionsPerIp < 0,
            $"MaxConnectionsPerIp must be >=0 (0 = unlimited, was {options.MaxConnectionsPerIp}).");
        FailWhen(options.WebsocketMaxMessageSize <= 0 || options.WebsocketMaxMessageSize > 64 * 1024 * 1024,
            $"WebsocketMaxMessageSize must be >0 and <=64MB (was {options.WebsocketMaxMessageSize}).");
        FailWhen(options.WebsocketMaxPendingSends <= 0 || options.WebsocketMaxPendingSends > 100000,
            $"WebsocketMaxPendingSends must be >0 and <=100000 (was {options.WebsocketMaxPendingSends}).");
        FailWhen(options.WebsocketMaxPendingBytes <= 0 || options.WebsocketMaxPendingBytes > 1024 * 1024 * 1024,
            $"WebsocketMaxPendingBytes must be >0 and <=1GB (was {options.WebsocketMaxPendingBytes}).");
        FailWhen(options.TelnetMaxPendingSends <= 0 || options.TelnetMaxPendingSends > 100000,
            $"TelnetMaxPendingSends must be >0 and <=100000 (was {options.TelnetMaxPendingSends}).");
        FailWhen(options.TelnetMaxPendingBytes <= 0 || options.TelnetMaxPendingBytes > 1024 * 1024 * 1024,
            $"TelnetMaxPendingBytes must be >0 and <=1GB (was {options.TelnetMaxPendingBytes}).");
        FailWhen(options.ConnectionInputQueueLimit <= 0 || options.ConnectionInputQueueLimit > 1000000,
            $"ConnectionInputQueueLimit must be >0 and <=1000000 (was {options.ConnectionInputQueueLimit}).");
        // connection/attempt/cooldown budgets must be non-negative
        // (0 = unlimited where the consumer documents it).
        FailWhen(options.MaxTotalConnections < 0,
            $"MaxTotalConnections must be >=0 (was {options.MaxTotalConnections}).");
        FailWhen(options.MaxLoginAttempts < 0,
            $"MaxLoginAttempts must be >=0 (was {options.MaxLoginAttempts}).");
        FailWhen(options.LoginAttemptCooldown < 0,
            $"LoginAttemptCooldown must be >=0 (was {options.LoginAttemptCooldown}).");
        FailWhen(options.CreationCooldown < 0,
            $"CreationCooldown must be >=0 (was {options.CreationCooldown}).");
        FailWhen(string.IsNullOrEmpty(options.FuncparserStartChar),
            "FuncparserStartChar must be non-empty.");
        FailWhen(string.IsNullOrEmpty(options.FuncparserEscapeChar),
            "FuncparserEscapeChar must be non-empty.");
        FailWhen(options.ThreadpoolQueueLimit <= 0,
            $"ThreadpoolQueueLimit must be >0 (was {options.ThreadpoolQueueLimit}).");
        FailWhen(options.ThreadpoolLimit is < 1,
            $"ThreadpoolLimit must be null or >=1 (was {options.ThreadpoolLimit}).");
        FailWhen(options.ThreadpoolReliefLimit is < 1,
            $"ThreadpoolReliefLimit must be null or >=1 (was {options.ThreadpoolReliefLimit}).");
        FailWhen(options.ThreadpoolWatchdogSeconds <= 0,
            $"ThreadpoolWatchdogSeconds must be >0 (was {options.ThreadpoolWatchdogSeconds}).");
        FailWhen(options.ThreadpoolWatchdogInterval <= 0,
            $"ThreadpoolWatchdogInterval must be >0 (was {options.ThreadpoolWatchdogInterval}).");
        FailWhen(options.FuncparserMaxNesting <= 0,
            $"FuncparserMaxNesting must be >0 (was {options.FuncparserMaxNesting}).");
        FailWhen(options.MapeditMaxChains <= 0,
            $"MapeditMaxChains must be >0 (was {options.MapeditMaxChains}).");
        FailWhen(options.MenuPromptTimeout <= 0,
            $"MenuPromptTimeout must be >0 (was {options.MenuPromptTimeout}).");
        CollectGuard(() => Atheriz.Core.Utils.PathGuards.GuardSavePath(options.SavePath), ex => $"SavePath invalid: {ex.Message}");
        CollectGuard(() => Atheriz.Core.Utils.PathGuards.GuardSecretPath(options.SecretPath), ex => $"SecretPath invalid: {ex.Message}");
        FailWhen(options.ChannelHistoryLimit <= 0,
            $"ChannelHistoryLimit must be >0 (was {options.ChannelHistoryLimit}).");

        FailWhen(options.MaxAccountNameLength < 3,
            $"MaxAccountNameLength must be >=3 (was {options.MaxAccountNameLength}).");
        FailWhen(options.MaxCharacterNameLength < 3,
            $"MaxCharacterNameLength must be >=3 (was {options.MaxCharacterNameLength}).");
        FailWhen(options.MinPasswordLength < 1,
            $"MinPasswordLength must be >=1 (was {options.MinPasswordLength}).");
        FailWhen(options.MaxPasswordLength < options.MinPasswordLength,
            $"MaxPasswordLength ({options.MaxPasswordLength}) must be >= MinPasswordLength ({options.MinPasswordLength}).");

        // No privileged-port floor: Python accepts 80/443 (bind may need
        // elevation, but that is the operator's call, not a config error).
        FailWhen(options.WebserverPort < 1 || options.WebserverPort > 65535,
            $"WebserverPort must be 1-65535 (was {options.WebserverPort}).");
        // Mirror the Kestrel/telnet fail-fast binds: "::" is the IPv6-any
        // spelling, anything else must parse as an IP literal.
        FailWhen(!IsValidInterface(options.WebserverInterface),
            $"WebserverInterface unparseable: '{options.WebserverInterface}'.");
        FailWhen(!IsValidInterface(options.TelnetInterface),
            $"TelnetInterface unparseable: '{options.TelnetInterface}'.");
        FailWhen(options.TelnetEnabled && options.WebserverPort == options.TelnetPort,
            $"WebserverPort ({options.WebserverPort}) must differ from TelnetPort ({options.TelnetPort}).");
        FailWhen(string.IsNullOrWhiteSpace(options.ServerName),
            "ServerName must be non-empty.");
        FailWhen(options.MinutesPerHour <= 0,
            $"MinutesPerHour must be >0 (was {options.MinutesPerHour}).");
        FailWhen(options.HoursPerDay <= 0,
            $"HoursPerDay must be >0 (was {options.HoursPerDay}).");
        FailWhen(options.DaysPerMonth <= 0,
            $"DaysPerMonth must be >0 (was {options.DaysPerMonth}).");
        FailWhen(options.MonthsPerYear <= 0,
            $"MonthsPerYear must be >0 (was {options.MonthsPerYear}).");
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
            if (options.TelnetPort < 1 || options.TelnetPort > 65535)
                failures.Add($"TelnetPort must be 1-65535 when TelnetEnabled (was {options.TelnetPort}).");
        }
        else if (options.TelnetPort < 1 || options.TelnetPort > 65535)
            failures.Add($"TelnetPort must be 1-65535 for later re-enable (was {options.TelnetPort}).");

        FailWhen(options.TelnetNawsMaxCols <= 0,
            $"TelnetNawsMaxCols must be >0 (was {options.TelnetNawsMaxCols}).");
        FailWhen(options.TelnetNawsMaxRows <= 0,
            $"TelnetNawsMaxRows must be >0 (was {options.TelnetNawsMaxRows}).");
        FailWhen(options.TelnetNawsMinCols <= 0,
            $"TelnetNawsMinCols must be >0 (was {options.TelnetNawsMinCols}).");
        FailWhen(options.TelnetNawsMinRows <= 0,
            $"TelnetNawsMinRows must be >0 (was {options.TelnetNawsMinRows}).");
        FailWhen(options.TelnetNawsMaxCols < options.TelnetNawsMinCols,
            $"TelnetNawsMaxCols ({options.TelnetNawsMaxCols}) must be >= TelnetNawsMinCols ({options.TelnetNawsMinCols}).");
        FailWhen(options.TelnetNawsMaxRows < options.TelnetNawsMinRows,
            $"TelnetNawsMaxRows ({options.TelnetNawsMaxRows}) must be >= TelnetNawsMinRows ({options.TelnetNawsMinRows}).");

        FailWhen(options.TelnetConnectionTimeout <= 0,
            $"TelnetConnectionTimeout must be >0 (was {options.TelnetConnectionTimeout}).");
        FailWhen(options.TelnetMaxLine <= 0,
            $"TelnetMaxLine must be >0 (was {options.TelnetMaxLine}).");
        FailWhen(options.MaxSearchDepth <= 0,
            $"MaxSearchDepth must be >0 (was {options.MaxSearchDepth}).");
        FailWhen(options.MaxAstarIterations <= 0,
            $"MaxAstarIterations must be >0 (was {options.MaxAstarIterations}).");
        FailWhen(string.IsNullOrWhiteSpace(options.LogLevel) || !ValidLogLevels.Contains(options.LogLevel.Trim()),
            $"LogLevel must be one of debug/info/warning/error/critical (was '{options.LogLevel}').");
        FailWhen(options.SecondsPerMinute <= 0,
            $"SecondsPerMinute must be >0 (was {options.SecondsPerMinute}).");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(string.Join("; ", failures));
    }
}
