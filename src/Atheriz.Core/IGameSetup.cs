namespace Atheriz.Core;

/// <summary>
/// World-setup parameters for CLI world creation (<c>reset</c>/<c>new</c>):
/// one record instead of five positional strings (swapping two strings no
/// longer compiles silently).
/// </summary>
public sealed record SetupOptions(
    string SavePath,
    string? Username = null,
    string? Password = null,
    string? SecretPath = null,
    bool Prompt = true,
    TextReader? Input = null);

/// <summary>
/// Game-side world-setup entry for CLI world creation (<c>reset</c>/<c>new</c>).
/// A game plugin provides an implementation; discovery instantiates the
/// entry and hands it back, and CLI call sites pass it explicitly to
/// <see cref="InitialSetup.RunSetup"/> — the engine keeps no static slot
/// and never names game types.
/// Null (the default) selects the engine template setup. Read only in
/// short-lived CLI processes, never on the server hot path.
/// </summary>
public interface IGameSetup
{
    void DoSetup(SetupOptions options);
}
