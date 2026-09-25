using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Dto;

public static class GameObjectDtoSerializer
{
    private static JsonSerializerOptions JsonOpts => JsonOptions.Default;
    // Volatile + single capture: a seam swap between the null check
    // and the invoke NREs, and a mid-checkpoint swap serializes rows in two
    // dialects. Each call observes exactly one generation.
    public static volatile Func<GameObjectDto, string>? ToJsonHook;
    public static volatile Func<string, GameObjectDto>? FromJsonHook;

    public static string ToJson(GameObjectDto dto)
    {
        var hook = ToJsonHook;
        if (hook is not null) return hook(dto);
        return JsonSerializer.Serialize(dto, JsonOpts);
    }
    public static GameObjectDto FromJson(string json)
    {
        var hook = FromJsonHook;
        if (hook is not null) return hook(json);
        return JsonSerializer.Deserialize<GameObjectDto>(json, JsonOpts)
            ?? throw new InvalidDataException("Failed to deserialize GameObjectDto");
    }
    public static GameObjectDto Migrate(GameObjectDto dto)
    {
        // v1 is current; future migrators switch on SchemaVersion
        // Backfill analogous to __setstate__ FLAG_DEFAULTS. Locks/Channels and
        // Location/Home predate the backfill list but need it too: an explicit
        // JSON null overwrites the property initializers and old saves with
        // nulls throw downstream instead of migrating.
        dto.Tags ??= [];
        dto.Aliases ??= [];
        dto.Contents ??= [];
        dto.Extra ??= [];
        dto.Locks ??= [];
        dto.Channels ??= [];
        dto.Location ??= LocationRef.NullLocation.Instance;
        dto.Home ??= LocationRef.NullLocation.Instance;
        return dto;
    }
}
