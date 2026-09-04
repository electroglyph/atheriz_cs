using System.Text.RegularExpressions;

namespace Atheriz.Core.Settings;

/// <summary>
/// Single source of truth for account/character/password validation shared by the
/// <c>/_internal/create_account</c> route and any other creation path.
/// Message strings are stable API (asserted by Ported tests); keep verbatim.
/// </summary>
public static class AccountValidation
{
    public static string? ValidateAccountName(string name, AtherizSettings s)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Account name must not be empty.";
        if (name.Length > s.MaxAccountNameLength) return $"Account name too long (max {s.MaxAccountNameLength}).";
        if (name.Length < 2) return "Account name too short.";
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_]+$")) return "Account name must be alphanumeric/underscore.";
        return null;
    }

    public static string? ValidateCharacterName(string name, AtherizSettings s)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Character name must not be empty.";
        if (name.Length > s.MaxCharacterNameLength) return $"Character name too long (max {s.MaxCharacterNameLength}).";
        if (name.Length < 2) return "Character name too short.";
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_]+$")) return "Character name must be alphanumeric/underscore.";
        return null;
    }

    public static string? ValidatePassword(string pw, AtherizSettings s)
    {
        if (pw.Length < s.MinPasswordLength) return $"Password too short (min {s.MinPasswordLength}).";
        if (pw.Length > s.MaxPasswordLength) return $"Password too long (max {s.MaxPasswordLength}).";
        return null;
    }
}
