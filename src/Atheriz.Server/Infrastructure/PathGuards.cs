// Port of atheriz/database_setup.py:66 + atheriz/atheriz.py:508,559 — thin wrapper delegating to Core
using CoreGuards = Atheriz.Core.Utils.PathGuards;

namespace Atheriz.Server.Infrastructure;

/// <summary>
/// Server wrapper for <see cref="CoreGuards"/> — keeps existing import paths working.
/// Mirrors <c>atheriz/database_setup.py:66</c> and <c>atheriz/atheriz.py:508,559</c>.
/// </summary>
public static class PathGuards
{
    public static void GuardSavePath(string savePath) => CoreGuards.GuardSavePath(savePath);
    public static void GuardSecretPath(string secretPath) => CoreGuards.GuardSecretPath(secretPath);
    public static void EnsureSaveDirectory(string savePath) => CoreGuards.EnsureSaveDirectory(savePath);
    public static void EnsureSecretDirectory(string secretPath) => CoreGuards.EnsureSecretDirectory(secretPath);
}
