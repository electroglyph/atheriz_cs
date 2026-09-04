using System.Text.Json;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Dto;

/// <summary>
/// Versioned DTO for any persisted entity (Object, Account, Channel, Script).
/// Replaces Python's <c>dill.dumps(self)</c> opaque blob with typed JSON.
/// On load, <c>SchemaVersion</c> selects migrator path — mirrors
/// <c>__setstate__</c> backfill via <c>FLAG_DEFAULTS</c>.
/// </summary>
public sealed class GameObjectDto
{
    public int Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Type { get; set; } = "object"; // object|account|channel|script|node
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public HashSet<string> Tags { get; set; } = [];

    // Flags subset — expand as object model grows
    public bool IsPc { get; set; }
    public bool IsNpc { get; set; }
    public bool IsItem { get; set; }
    public bool IsContainer { get; set; }
    public bool IsMapable { get; set; }
    public bool IsNode { get; set; }
    public bool IsTemporary { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsModified { get; set; } = true;

    public Privilege PrivilegeLevel { get; set; } = Privilege.Guest;
    public string Gender { get; set; } = "neutral";

    public LocationRef Location { get; set; } = LocationRef.NullLocation.Instance;
    public LocationRef Home { get; set; } = LocationRef.NullLocation.Instance;

    public HashSet<int> Contents { get; set; } = [];
    public HashSet<int> Scripts { get; set; } = [];
    public List<LockDefDto> Locks { get; set; } = [];
    public List<int> Channels { get; set; } = [];

    // Arbitrary extra fields (setattr dynamics) — stored as JSON extension
    public Dictionary<string, JsonElement> Extra { get; set; } = new();

    // Helpers
    public static GameObjectDto Create(int id, string name, string type = "object")
        => new() { Id = id, Name = name, Type = type };
}

public sealed class LockDefDto
{
    public string Name { get; set; } = ""; // e.g. "view", "get", "delete", "puppet"
    public string Policy { get; set; } = ""; // declarative, e.g. "is_builder", "self_only"
}

public static class GameObjectDtoSerializer
{
    private static JsonSerializerOptions JsonOpts => JsonOptions.Default;
    // Test hooks for lock-held verification (port of dill.dumps/loads monkeypatch)
    public static Func<GameObjectDto, string>? ToJsonHook;
    public static Func<string, GameObjectDto>? FromJsonHook;

    public static string ToJson(GameObjectDto dto)
    {
        if (ToJsonHook != null) return ToJsonHook(dto);
        return JsonSerializer.Serialize(dto, JsonOpts);
    }
    public static GameObjectDto FromJson(string json)
    {
        if (FromJsonHook != null) return FromJsonHook(json);
        return JsonSerializer.Deserialize<GameObjectDto>(json, JsonOpts)
            ?? throw new InvalidDataException("Failed to deserialize GameObjectDto");
    }
    public static GameObjectDto Migrate(GameObjectDto dto)
    {
        // v1 is current; future migrators switch on SchemaVersion
        // Backfill analogous to __setstate__ FLAG_DEFAULTS
        dto.Tags ??= [];
        dto.Aliases ??= [];
        dto.Contents ??= [];
        dto.Extra ??= [];
        return dto;
    }
}
