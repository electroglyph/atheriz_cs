using System.Text.Json;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Network;

/// <summary>
/// Validated map-editor chrome sent as the optional fourth <c>map_edit</c> arg
/// on explicit save-to-server and stored on the granting session's account.
/// Property names serialize camelCase (via <see cref="JsonOptions.Default"/>)
/// to match the webclient <c>EditorSettings</c> shape exactly.
/// </summary>
public sealed record MapEditorSettings(
    int[] FgColor,
    int[] BgColor,
    List<int[]> FgSlots,
    List<int[]> BgSlots,
    List<int[]> GradientStops,
    List<string> CustomChars,
    string SelectedChar,
    string FontFamily,
    int FontSize,
    MapEditorSettings.ToolSettings Tools,
    int[] RoomColor,
    bool RoomVisible)
{
    /// <summary>
    /// Tool-mode half of the settings (mirrors the client's AppState tool fields).
    /// </summary>
    public sealed record ToolSettings(
        string ActiveToolId,
        string RectMode,
        string OvalMode,
        string LineMode,
        bool LineDiagonal,
        string GradientTarget,
        string FillMode,
        string EyedropperTarget,
        string SelectMode,
        string RotateMode,
        string TypeStyle);

    // Wire bounds: the client sends fixed-size slot histories (8 each) and at
    // least 2 gradient stops; the caps below bound hostile payloads while
    // leaving headroom over every legitimate client value.
    private const int MaxSlots = 64;
    private const int MinGradientStops = 2;
    private const int MaxGradientStops = 64;
    private const int MaxCustomChars = 512;
    private const int MaxCustomCharLength = 8;
    private const int MaxSelectedCharLength = 8;
    private const int MaxFontFamilyLength = 128;
    private const int MaxFontSize = 256;
    private const int MaxToolIdLength = 64;

    // Allowed tool modes mirror the webclient types.ts unions exactly, so a
    // client/server mode drift fails closed with the field named.
    private static readonly string[] RectModes = ["light", "rounded", "double", "heavy", "custom"];
    private static readonly string[] OvalModes = ["light", "rounded", "double", "heavy", "circle", "custom"];
    private static readonly string[] LineModes = ["light", "rounded", "double", "heavy", "custom"];
    private static readonly string[] GradientTargets = ["foreground", "background", "both", "luminance", "inverse-luminance"];
    private static readonly string[] FillModes = ["brush", "foreground", "background", "gradient"];
    private static readonly string[] EyedropperTargets = ["fg-fg", "fg-bg", "bg-fg", "bg-bg"];
    private static readonly string[] SelectModes = ["single", "rectangle", "lasso", "magic", "color-match", "color-fuzzy"];
    private static readonly string[] RotateModes = ["cw90", "ccw90", "180", "flip-h", "flip-v", "free"];
    private static readonly string[] TypeStyles = ["regular", "bold", "italic", "underline"];

    /// <summary>
    /// Converted <c>Dictionary</c> form and the raw <c>JsonElement</c> form
    /// (same dual-shape tolerance as the cell/legend validators). Unknown keys
    /// are ignored; every documented key is required. False names the failure.
    /// </summary>
    public static bool TryParse(object? raw, out MapEditorSettings? settings, out string error)
    {
        settings = null;
        if (NormalizeDict(raw) is not { } dict) { error = "settings must be an object"; return false; }
        if (!Require(dict, "fgColor", out var fgRaw, out error)) return false;
        if (!TryGetColor(fgRaw, out var fgColor)) { error = "'fgColor' must be an [r,g,b] triple of 0-255 integers"; return false; }
        if (!Require(dict, "bgColor", out var bgRaw, out error)) return false;
        if (!TryGetColor(bgRaw, out var bgColor)) { error = "'bgColor' must be an [r,g,b] triple of 0-255 integers"; return false; }
        if (!Require(dict, "fgSlots", out var fgSlotsRaw, out error)) return false;
        if (!TryGetColorList(fgSlotsRaw, 1, MaxSlots, out var fgSlots)) { error = $"'fgSlots' must be a list of 1-{MaxSlots} colors"; return false; }
        if (!Require(dict, "bgSlots", out var bgSlotsRaw, out error)) return false;
        if (!TryGetColorList(bgSlotsRaw, 1, MaxSlots, out var bgSlots)) { error = $"'bgSlots' must be a list of 1-{MaxSlots} colors"; return false; }
        if (!Require(dict, "gradientStops", out var stopsRaw, out error)) return false;
        if (!TryGetColorList(stopsRaw, MinGradientStops, MaxGradientStops, out var gradientStops)) { error = $"'gradientStops' must be a list of {MinGradientStops}-{MaxGradientStops} colors"; return false; }
        if (!TryGetStringList(dict, "customChars", MaxCustomChars, MaxCustomCharLength, out var customChars, out error)) return false;
        if (!Require(dict, "selectedChar", out var selectedRaw, out error)) return false;
        if (AsString(selectedRaw) is not { Length: >= 1 and <= MaxSelectedCharLength } selectedChar) { error = $"'selectedChar' must be a 1-{MaxSelectedCharLength} character string"; return false; }
        if (!Require(dict, "fontFamily", out var fontRaw, out error)) return false;
        if (AsString(fontRaw) is not { Length: >= 1 and <= MaxFontFamilyLength } fontFamily) { error = $"'fontFamily' must be a 1-{MaxFontFamilyLength} character string"; return false; }
        if (!Require(dict, "fontSize", out var fontSizeRaw, out error)) return false;
        if (!TryGetInt(fontSizeRaw, out var fontSize) || fontSize < 1 || fontSize > MaxFontSize) { error = $"'fontSize' must be an integer 1-{MaxFontSize}"; return false; }
        if (!Require(dict, "tools", out var toolsRaw, out error)) return false;
        if (NormalizeDict(toolsRaw) is not { } toolsDict) { error = "'tools' must be an object"; return false; }
        if (!TryGetTools(toolsDict, out var tools, out error)) return false;
        if (!Require(dict, "roomColor", out var roomRaw, out error)) return false;
        if (!TryGetColor(roomRaw, out var roomColor)) { error = "'roomColor' must be an [r,g,b] triple of 0-255 integers"; return false; }
        if (!Require(dict, "roomVisible", out var visibleRaw, out error)) return false;
        if (!TryGetBool(visibleRaw, out var roomVisible)) { error = "'roomVisible' must be a boolean"; return false; }
        error = "";
        settings = new MapEditorSettings(fgColor, bgColor, fgSlots, bgSlots, gradientStops, customChars, selectedChar, fontFamily, fontSize, tools, roomColor, roomVisible);
        return true;
    }

    /// <summary>
    /// Canonical wire/persist form: camelCase keys, unknown keys dropped,
    /// number types unified. This is what the account stores.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);

    private static bool Require(Dictionary<string, object?> dict, string key, out object? value, out string error)
    {
        if (!dict.TryGetValue(key, out value) || value is null)
        {
            value = null;
            error = $"missing required key '{key}'";
            return false;
        }
        error = "";
        return true;
    }

    private static bool TryGetTools(Dictionary<string, object?> tools, out ToolSettings parsed, out string error)
    {
        parsed = null!;
        if (!Require(tools, "activeToolId", out var idRaw, out error)) { PrependToolsPrefix(ref error); return false; }
        if (AsString(idRaw) is not { Length: >= 1 and <= MaxToolIdLength } activeToolId) { error = $"'tools.activeToolId' must be a 1-{MaxToolIdLength} character string"; return false; }
        if (!TryGetMode(tools, "rectMode", RectModes, out var rectMode, out error)) return false;
        if (!TryGetMode(tools, "ovalMode", OvalModes, out var ovalMode, out error)) return false;
        if (!TryGetMode(tools, "lineMode", LineModes, out var lineMode, out error)) return false;
        if (!Require(tools, "lineDiagonal", out var diagRaw, out error)) { PrependToolsPrefix(ref error); return false; }
        if (!TryGetBool(diagRaw, out var lineDiagonal)) { error = "'tools.lineDiagonal' must be a boolean"; return false; }
        if (!TryGetMode(tools, "gradientTarget", GradientTargets, out var gradientTarget, out error)) return false;
        if (!TryGetMode(tools, "fillMode", FillModes, out var fillMode, out error)) return false;
        if (!TryGetMode(tools, "eyedropperTarget", EyedropperTargets, out var eyedropperTarget, out error)) return false;
        if (!TryGetMode(tools, "selectMode", SelectModes, out var selectMode, out error)) return false;
        if (!TryGetMode(tools, "rotateMode", RotateModes, out var rotateMode, out error)) return false;
        if (!TryGetMode(tools, "typeStyle", TypeStyles, out var typeStyle, out error)) return false;
        error = "";
        parsed = new ToolSettings(activeToolId, rectMode, ovalMode, lineMode, lineDiagonal, gradientTarget, fillMode, eyedropperTarget, selectMode, rotateMode, typeStyle);
        return true;
    }

    // A missing tools key reports as "missing required key 'rectMode'"; the
    // tools. prefix belongs on every tools failure alike.
    private static void PrependToolsPrefix(ref string error)
    {
        const string marker = "missing required key '";
        if (error.StartsWith(marker, StringComparison.Ordinal) && error.EndsWith("'", StringComparison.Ordinal))
            error = $"missing required key 'tools.{error.Substring(marker.Length, error.Length - marker.Length - 1)}'";
    }

    private static bool TryGetMode(Dictionary<string, object?> tools, string key, string[] allowed, out string mode, out string error)
    {
        mode = "";
        if (!Require(tools, key, out var raw, out error)) { PrependToolsPrefix(ref error); return false; }
        var s = AsString(raw);
        if (s is null || !allowed.Contains(s, StringComparer.Ordinal)) { error = $"'tools.{key}' must be one of: {string.Join(", ", allowed)}"; return false; }
        mode = s;
        error = "";
        return true;
    }

    private static bool TryGetStringList(Dictionary<string, object?> dict, string key, int maxCount, int maxLength, out List<string> parsed, out string error)
    {
        parsed = [];
        if (!Require(dict, key, out var raw, out error)) return false;
        if (NormalizeList(raw) is not { } list || list.Count > maxCount) { error = $"'{key}' must be a list of 0-{maxCount} strings"; return false; }
        var result = new List<string>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var s = AsString(list[i]);
            if (s is null || s.Length < 1 || s.Length > maxLength) { error = $"'{key}[{i}]' must be a 1-{maxLength} character string"; return false; }
            result.Add(s);
        }
        parsed = result;
        error = "";
        return true;
    }

    private static bool TryGetColor(object? v, out int[] color)
    {
        color = [];
        if (NormalizeList(v) is not { Count: 3 } list) return false;
        var rgb = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!TryGetInt(list[i], out var c) || c < 0 || c > 255) return false;
            rgb[i] = c;
        }
        color = rgb;
        return true;
    }

    private static bool TryGetColorList(object? v, int min, int max, out List<int[]> colors)
    {
        colors = [];
        if (NormalizeList(v) is not { } list || list.Count < min || list.Count > max) return false;
        var parsed = new List<int[]>(list.Count);
        foreach (var item in list)
        {
            if (!TryGetColor(item, out var c)) return false;
            parsed.Add(c);
        }
        colors = parsed;
        return true;
    }

    // Dual-shape coercion mirrors the cell/legend validators: converted Lists
    // from the wire path and raw JsonElements from in-process callers.
    private static Dictionary<string, object?>? NormalizeDict(object? v)
    {
        if (v is Dictionary<string, object?> d) return d;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, object?> dict = [];
            foreach (var p in je.EnumerateObject()) dict[p.Name] = ConnectionManager.JsonElementToObject(p.Value);
            return dict;
        }
        return null;
    }

    private static List<object?>? NormalizeList(object? v)
    {
        if (v is List<object?> l) return l;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var list = new List<object?>(je.GetArrayLength());
            foreach (var item in je.EnumerateArray()) list.Add(ConnectionManager.JsonElementToObject(item));
            return list;
        }
        return null;
    }

    private static string? AsString(object? v) => v switch
    {
        string s => s,
        JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
        _ => null,
    };

    private static bool TryGetInt(object? v, out int i)
    {
        if (v is int iv) { i = iv; return true; }
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.TryGetInt32(out i);
        i = 0;
        return false;
    }

    private static bool TryGetBool(object? v, out bool b)
    {
        if (v is bool bv) { b = bv; return true; }
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) { b = true; return true; }
            if (je.ValueKind == JsonValueKind.False) { b = false; return true; }
        }
        b = false;
        return false;
    }
}
