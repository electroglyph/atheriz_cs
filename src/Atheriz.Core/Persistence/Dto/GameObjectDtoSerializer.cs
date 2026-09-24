using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Dto;

public static class GameObjectDtoSerializer
{
    private static JsonSerializerOptions JsonOpts => JsonOptions.Default;
    public static Func<GameObjectDto, string>? ToJsonHook;
    public static Func<string, GameObjectDto>? FromJsonHook;

    public static string ToJson(GameObjectDto dto)
    {
        if (ToJsonHook is not null) return ToJsonHook(dto);
        return JsonSerializer.Serialize(dto, JsonOpts);
    }
    public static GameObjectDto FromJson(string json)
    {
        if (FromJsonHook is not null) return FromJsonHook(json);
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
