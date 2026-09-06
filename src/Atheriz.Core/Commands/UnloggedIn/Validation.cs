using Atheriz.Core.Settings;
// Port of atheriz/commands/unloggedin/validation.py:38
using System.Text.RegularExpressions;

namespace Atheriz.Core.Commands.UnloggedIn;

public static class Validation
{
    private static readonly Regex NameRe = new(@"^[A-Za-z0-9 _'\-]+$", RegexOptions.Compiled);
    public static string? ValidateName(string name, int maxLen)
    {
        // Python parity: validate_name(None) crashes on None.strip(); the C#
        // equivalent fails fast with the parameter named (not a bare NRE).
        ArgumentNullException.ThrowIfNull(name);
        string stripped = name.Trim();
        if (string.IsNullOrEmpty(stripped)) return "Name cannot be empty.";
        if (stripped.Length < 3) return "Name must be at least 3 characters.";
        if (stripped.Length > maxLen) return $"Name must be at most {maxLen} characters.";
        if (name.Contains('\x1b') || name.Contains('\x00')) return "Name contains invalid characters.";
        if (!NameRe.IsMatch(stripped)) return "Name may only contain letters, digits, spaces, hyphens, underscores and apostrophes.";
        if (!stripped.Any(char.IsLetter)) return "Name must contain at least one letter.";
        if (stripped.Contains("  ")) return "Name cannot contain consecutive spaces.";
        return null;
    }
    public static string? ValidateAccountName(string name) => ValidateName(name, AtherizSettings.Global.MaxAccountNameLength);
    public static string? ValidateCharacterName(string name) => ValidateName(name, AtherizSettings.Global.MaxCharacterNameLength);
    // Settings-aware overloads (single source of truth, audit D1 — the
    // duplicate Settings/AccountValidation.cs is deleted).
    public static string? ValidateAccountName(string name, AtherizSettings s) => ValidateName(name, s.MaxAccountNameLength);
    public static string? ValidateCharacterName(string name, AtherizSettings s) => ValidateName(name, s.MaxCharacterNameLength);
    public static string? ValidatePassword(string password, AtherizSettings s)
    {
        if (string.IsNullOrEmpty(password)) return "Password cannot be empty.";
        if (password.Length < s.MinPasswordLength) return $"Password must be at least {s.MinPasswordLength} characters.";
        if (password.Length > s.MaxPasswordLength) return $"Password must be at most {s.MaxPasswordLength} characters.";
        return null;
    }
    public static string? ValidatePassword(string password)
    {
        var settings = AtherizSettings.Global;
        return ValidatePassword(password, settings);
    }
}
