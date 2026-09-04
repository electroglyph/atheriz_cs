using Atheriz.Core.Settings;
// Port of atheriz/commands/unloggedin/validation.py:38
using System.Text.RegularExpressions;

namespace Atheriz.Core.Commands.UnloggedIn;

public static class Validation
{
    private static readonly Regex NameRe = new(@"^[A-Za-z0-9 _'\-]+$", RegexOptions.Compiled);
    public static string? ValidateName(string name, int maxLen)
    {
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
    public static string? ValidatePassword(string password)
    {
        var settings = AtherizSettings.Global;
        if (string.IsNullOrEmpty(password)) return "Password cannot be empty.";
        if (password.Length < settings.MinPasswordLength) return $"Password must be at least {settings.MinPasswordLength} characters.";
        if (password.Length > settings.MaxPasswordLength) return $"Password must be at most {settings.MaxPasswordLength} characters.";
        return null;
    }
}
