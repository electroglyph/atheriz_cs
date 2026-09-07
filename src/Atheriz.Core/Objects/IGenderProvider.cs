namespace Atheriz.Core.Objects;

/// <summary>
/// Dynamic-gender contract (port of callable gender): when an object implements
/// this, FuncParser pronoun/conjugation resolution calls <see cref="GetGender"/>
/// instead of reading the <c>Gender</c> string. Game code with computed gender
/// implements this rather than shadowing the property.
/// </summary>
public interface IGenderProvider
{
    string? GetGender();
}
