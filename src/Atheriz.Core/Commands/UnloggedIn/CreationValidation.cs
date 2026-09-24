namespace Atheriz.Core.Commands.UnloggedIn;

// Single truth for the "character name taken" pre-check shared by the
// guest/new verbs. (The create verb checks account names instead, so it
// keeps its own inline check.)
public static class CreationValidation
{
    public static bool PcNameExists(string name)
        => ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Count > 0;
}
