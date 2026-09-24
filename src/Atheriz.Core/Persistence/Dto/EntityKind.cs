namespace Atheriz.Core.Persistence.Dto;

/// <summary>
/// Typed entity kind. Persisted shape stays the string <see cref="GameObjectDto.Type"/>
/// plus <c>IsNode</c>; this enum is the single classify point so converters do not
/// juggle two sources for kind.
/// </summary>
public enum EntityKind
{
    Object,
    Account,
    Channel,
    Script,
    Node,
}

public static class EntityKinds
{
    public static EntityKind Classify(GameObjectDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.Equals(dto.Type, "script", StringComparison.OrdinalIgnoreCase)) return EntityKind.Script;
        if (string.Equals(dto.Type, "channel", StringComparison.OrdinalIgnoreCase)) return EntityKind.Channel;
        if (string.Equals(dto.Type, "account", StringComparison.OrdinalIgnoreCase)) return EntityKind.Account;
        if (dto.IsNode) return EntityKind.Node;
        if (string.Equals(dto.Type, "node", StringComparison.OrdinalIgnoreCase)) return EntityKind.Node;
        return EntityKind.Object;
    }

    public static string Name(EntityKind kind) => kind switch
    {
        EntityKind.Script => "script",
        EntityKind.Channel => "channel",
        EntityKind.Account => "account",
        EntityKind.Node => "node",
        _ => "object",
    };
}
