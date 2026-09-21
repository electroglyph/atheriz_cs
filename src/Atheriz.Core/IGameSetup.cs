namespace Atheriz.Core;

/// <summary>
/// Game-side world-setup entry for CLI world creation (<c>reset</c>/<c>new</c>).
/// A game plugin provides an implementation and self-registers it on
/// <see cref="InitialSetup.GameSetup"/> (e.g. via module initializer) so the
/// engine never names game types and needs no reflection to reach them.
/// Null (the default) selects the engine template setup. Read only in
/// short-lived CLI processes, never on the server hot path.
/// </summary>
public interface IGameSetup
{
    void DoSetup(string savePath, string? username, string? password, string? secretPath, bool prompt);
}
