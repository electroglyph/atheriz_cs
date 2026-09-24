namespace Atheriz.Core.Objects;

// Node-owned settable properties (ISettable): the base switch handles the
// shared properties, this override only adds the node extras.
public partial class Node
{
    /// <inheritdoc/>
    public override bool IsKnownProperty(string name) => name switch
    {
        "Theme" or "theme" or "_theme" => true,
        "LegendDesc" or "legend_desc" or "_legend_desc" => true,
        "OpenAttenuation" or "open_attenuation" or "_open_attenuation" => true,
        "EnclosedAttenuation" or "enclosed_attenuation" or "_enclosed_attenuation" => true,
        "AmbientSoundLevel" or "ambient_sound_level" or "_ambient_sound_level" => true,
        "Coord" or "coord" or "_coord" => true,
        "Links" or "links" or "_links" => true,
        "Nouns" or "nouns" or "_nouns" => true,
        "ScriptsSet" or "scripts_set" or "_scripts_set" => true,
        _ => base.IsKnownProperty(name),
    };

    /// <inheritdoc/>
    public override bool TrySetProperty(string name, object? value, out string? error)
    {
        error = null;
        switch (name)
        {
            case "Theme" or "theme" or "_theme":
                // Null passes into the non-nullable property like the old
                // (string) cast did; the compiler cannot see that flow.
                Theme = ToText(value)!;
                return true;
            case "LegendDesc" or "legend_desc" or "_legend_desc":
                LegendDesc = ToText(value);
                return true;
            case "OpenAttenuation" or "open_attenuation" or "_open_attenuation":
                OpenAttenuation = ToDouble(value);
                return true;
            case "EnclosedAttenuation" or "enclosed_attenuation" or "_enclosed_attenuation":
                EnclosedAttenuation = ToDouble(value);
                return true;
            case "AmbientSoundLevel" or "ambient_sound_level" or "_ambient_sound_level":
                AmbientSoundLevel = ToDouble(value);
                return true;
            case "Coord" or "coord" or "_coord":
                // Coord is a struct: null unboxes exactly like the old
                // (Coord) cast did, anything but a coord throws.
                Coord = value is null ? UnboxNull<Coord>() : value is Coord c ? c : throw new InvalidCastException();
                return true;
            case "Links" or "links" or "_links":
                // Null reaches the setter's own null guard like the old
                // cast did; the compiler cannot see that flow.
                Links = value is null ? null! : value is List<NodeLink> l ? l : throw new InvalidCastException();
                return true;
            case "Nouns" or "nouns" or "_nouns":
                Nouns = value is null ? null! : value is Dictionary<string, string> n ? n : throw new InvalidCastException();
                return true;
            case "ScriptsSet" or "scripts_set" or "_scripts_set":
                error = $"'{name}' is a read-only attribute.";
                return false;
            default:
                return base.TrySetProperty(name, value, out error);
        }
    }
}
