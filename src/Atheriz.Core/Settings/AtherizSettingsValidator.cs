using Microsoft.Extensions.Options;

namespace Atheriz.Core.Settings;

/// <summary>
/// Validates <see cref="AtherizSettings"/> on startup via <see cref="IValidateOptions{TOptions}"/>.
/// Ports the implicit invariants from <c>atheriz/settings.py</c> that were previously unchecked.
/// </summary>
public sealed class AtherizSettingsValidator : IValidateOptions<AtherizSettings>
{
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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(string.Join("; ", failures));
    }
}
