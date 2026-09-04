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
        // Stricter upper bound previously documented as 10; we enforce <=100 to remain permissive.
        // If you need the stricter 10 limit, change to >10.
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
