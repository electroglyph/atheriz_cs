namespace Atheriz.Core;

/// <summary>
/// Mirrors <c>atheriz/settings.py:Privilege</c>. Values 1..5.
/// </summary>
public enum Privilege
{
    Guest = 1,
    Player = 2,
    Helper = 3,
    Builder = 4,
    Admin = 5,
}
// NOTE (F016): the game-calendar Month enum used to live here; it now lives next to its
// only consumer in Globals/GameTime.cs (same namespace, so no using changes).
