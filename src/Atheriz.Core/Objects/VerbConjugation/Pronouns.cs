// Port of atheriz/objects/verb_conjugation/pronouns.py:1
using System.Collections.Generic;
using System.Linq;

namespace Atheriz.Core.Objects.VerbConjugation;

/// <summary>
/// Port of <c>atheriz/objects/verb_conjugation/pronouns.py</c> (299 LOC).
/// Evennia BSD mapping 1st/2nd ↔ 3rd with viewpoint/pronoun_type/gender disambiguation.
/// </summary>
public static class Pronouns
{
    public const string DefaultPronounType = "subject pronoun";
    public const string DefaultViewpoint = "2nd person";
    public const string DefaultGender = "neutral";

    public static readonly string[] PronounTypes =
    [
        "subject pronoun",
        "object pronoun",
        "possessive adjective",
        "possessive pronoun",
        "reflexive pronoun",
    ];
    public static readonly string[] Viewpoints = ["1st person", "2nd person", "3rd person"];
    public static readonly string[] Genders = ["male", "female", "neutral", "plural"];

    // PRONOUN_MAPPING
    public static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> PronounMapping =
        new(StringComparer.Ordinal)
        {
            ["1st person"] = new()
            {
                ["subject pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="I", ["plural"]="we" },
                ["object pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="me", ["plural"]="us" },
                ["possessive adjective"] = new(StringComparer.Ordinal){ ["neutral"]="my", ["plural"]="our" },
                ["possessive pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="mine", ["plural"]="ours" },
                ["reflexive pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="myself", ["plural"]="ourselves" },
            },
            ["2nd person"] = new()
            {
                ["subject pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="you" },
                ["object pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="you" },
                ["possessive adjective"] = new(StringComparer.Ordinal){ ["neutral"]="your" },
                ["possessive pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="yours" },
                ["reflexive pronoun"] = new(StringComparer.Ordinal){ ["neutral"]="yourself", ["plural"]="yourselves" },
            },
            ["3rd person"] = new()
            {
                ["subject pronoun"] = new(StringComparer.Ordinal){ ["male"]="he", ["female"]="she", ["neutral"]="it", ["plural"]="they" },
                ["object pronoun"] = new(StringComparer.Ordinal){ ["male"]="him", ["female"]="her", ["neutral"]="it", ["plural"]="them" },
                ["possessive adjective"] = new(StringComparer.Ordinal){ ["male"]="his", ["female"]="her", ["neutral"]="its", ["plural"]="their" },
                ["possessive pronoun"] = new(StringComparer.Ordinal){ ["male"]="his", ["female"]="hers", ["neutral"]="its", ["plural"]="theirs" },
                ["reflexive pronoun"] = new(StringComparer.Ordinal){ ["male"]="himself", ["female"]="herself", ["neutral"]="itself", ["plural"]="themselves" },
            },
        };

    // PRONOUN_TABLE: pronoun lower -> (viewpoint, gender(s), pronoun_type(s))
    public static readonly Dictionary<string, (string viewpoint, object gender, object pronType)> PronounTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["I"] = ("1st person", (object)new[] { "neutral","male","female","plural" }, (object)"subject pronoun"),
            ["me"] = ("1st person", (object)new[] { "neutral","male","female","plural" }, (object)"object pronoun"),
            ["my"] = ("1st person", (object)new[] { "neutral","male","female","plural" }, (object)"possessive adjective"),
            ["mine"] = ("1st person", (object)new[] { "neutral","male","female","plural" }, (object)"possessive pronoun"),
            ["myself"] = ("1st person", (object)new[] { "neutral","male","female","plural" }, (object)"reflexive pronoun"),
            ["we"] = ("1st person", (object)"plural", (object)"subject pronoun"),
            ["us"] = ("1st person", (object)"plural", (object)"object pronoun"),
            ["our"] = ("1st person", (object)"plural", (object)"possessive adjective"),
            ["ours"] = ("1st person", (object)"plural", (object)"possessive pronoun"),
            ["ourselves"] = ("1st person", (object)"plural", (object)"reflexive pronoun"),
            ["you"] = ("2nd person", (object)new[] { "neutral","male","female","plural" }, (object)new[] { "subject pronoun","object pronoun" }),
            ["your"] = ("2nd person", (object)new[] { "neutral","male","female","plural" }, (object)"possessive adjective"),
            ["yours"] = ("2nd person", (object)new[] { "neutral","male","female","plural" }, (object)"possessive pronoun"),
            ["yourself"] = ("2nd person", (object)new[] { "neutral","male","female" }, (object)"reflexive pronoun"),
            ["yourselves"] = ("2nd person", (object)"plural", (object)"reflexive pronoun"),
            ["he"] = ("3rd person", (object)"male", (object)"subject pronoun"),
            ["him"] = ("3rd person", (object)"male", (object)"object pronoun"),
            ["his"] = ("3rd person", (object)"male", (object)new[] { "possessive pronoun","possessive adjective" }),
            ["himself"] = ("3rd person", (object)"male", (object)"reflexive pronoun"),
            ["she"] = ("3rd person", (object)"female", (object)"subject pronoun"),
            ["her"] = ("3rd person", (object)"female", (object)new[] { "object pronoun","possessive adjective" }),
            ["hers"] = ("3rd person", (object)"female", (object)"possessive pronoun"),
            ["herself"] = ("3rd person", (object)"female", (object)"reflexive pronoun"),
            ["it"] = ("3rd person", (object)"neutral", (object)new[] { "subject pronoun","object pronoun" }),
            ["its"] = ("3rd person", (object)"neutral", (object)new[] { "possessive pronoun","possessive adjective" }),
            ["itself"] = ("3rd person", (object)"neutral", (object)"reflexive pronoun"),
            ["they"] = ("3rd person", (object)"plural", (object)"subject pronoun"),
            ["them"] = ("3rd person", (object)"plural", (object)"object pronoun"),
            ["their"] = ("3rd person", (object)"plural", (object)"possessive adjective"),
            ["theirs"] = ("3rd person", (object)"plural", (object)"possessive pronoun"),
            ["themselves"] = ("3rd person", (object)"plural", (object)"reflexive pronoun"),
        };

    public static readonly Dictionary<string, object> ViewpointConversion = new(StringComparer.Ordinal)
    {
        ["1st person"] = "3rd person",
        ["2nd person"] = "3rd person",
        ["3rd person"] = new[] { "2nd person", "1st person" },
    };

    public static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"]="male", ["f"]="female", ["n"]="neutral", ["p"]="plural",
        ["1st"]="1st person", ["2nd"]="2nd person", ["3rd"]="3rd person",
        ["1"]="1st person", ["2"]="2nd person", ["3"]="3rd person",
        ["s"]="subject pronoun", ["sp"]="subject pronoun", ["subject"]="subject pronoun",
        ["op"]="object pronoun", ["object"]="object pronoun",
        ["pa"]="possessive adjective", ["pp"]="possessive pronoun",
        ["adjective"]="possessive adjective", ["pronoun"]="possessive pronoun",
    };

    private static bool IsIter(object o) => o is System.Collections.IEnumerable && o is not string;

    private static string CopyWordCase(string src, string dst)
    {
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) return dst;
        // If src is all caps, return dst upper
        if (src.All(char.IsUpper)) return dst.ToUpperInvariant();
        // If src capitalized (first upper rest lower)
        if (char.IsUpper(src[0]) && src.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c)))
            return char.ToUpperInvariant(dst[0]) + (dst.Length > 1 ? dst[1..].ToLowerInvariant() : "");
        // If src lower, return lower
        return dst.ToLowerInvariant();
    }

    /// <summary>
    /// Port of <c>pronoun_to_viewpoints</c>. Returns (1st/2nd, 3rd) tuple.
    /// </summary>
    public static (string firstSecond, string third) PronounToViewpoints(
        string pronoun,
        object? options = null,
        string? pronounType = null,
        string? gender = null,
        string? viewpoint = null)
    {
        if (string.IsNullOrEmpty(pronoun)) return (pronoun, pronoun);
        var pronounLower = pronoun == "I" ? "I" : pronoun.ToLowerInvariant();
        if (!PronounTable.TryGetValue(pronounLower, out var entry))
            return (pronoun, pronoun);

        var (sourceViewpoint, sourceGender, sourceType) = entry;

        // defaults from source pronoun's attributes
        if (!PronounTypes.Contains(pronounType ?? ""))
        {
            if (sourceType is string s) pronounType = s;
            else if (sourceType is string[] arr) pronounType = arr[0];
            else pronounType = DefaultPronounType;
        }
        if (!Viewpoints.Contains(viewpoint ?? ""))
        {
            viewpoint = sourceViewpoint;
        }
        if (!Genders.Contains(gender ?? ""))
        {
            if (sourceGender is string sg) gender = sg;
            else if (sourceGender is string[] sga) gender = sga[0];
            else gender = DefaultGender;
        }

        if (options != null)
        {
            List<string> opts;
            if (options is string sopt)
                opts = sopt.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (options is IEnumerable<string> es)
                opts = es.ToList();
            else if (options is IEnumerable<object> eo)
                opts = eo.Select(o => o?.ToString() ?? "").ToList();
            else
                opts = new List<string> { options.ToString() ?? "" };

            opts = opts.Select(p => p.Trim().ToLowerInvariant()).Select(o => Aliases.TryGetValue(o, out var a) ? a : o).ToList();
            foreach (var opt in opts)
            {
                if (PronounTypes.Contains(opt)) pronounType = opt;
                else if (Viewpoints.Contains(opt)) viewpoint = opt;
                else if (Genders.Contains(opt)) gender = opt;
            }
        }

        // validate sourceType handling: if multiple options, narrow
        if (IsIter(sourceType))
        {
            var arr = sourceType is string[] sa ? sa : ((object[])sourceType).Cast<string>().ToArray();
            if (!arr.Contains(pronounType!)) pronounType = arr[0];
        }
        else
        {
            pronounType = (string)sourceType;
        }

        // viewpoint conversion
        var targetViewpointObj = ViewpointConversion[sourceViewpoint];
        string targetViewpoint;
        if (targetViewpointObj is string tStr)
        {
            viewpoint = tStr;
            targetViewpoint = tStr;
        }
        else
        {
            var arr = targetViewpointObj as string[];
            if (arr != null && arr.Contains(viewpoint!)) targetViewpoint = viewpoint!;
            else targetViewpoint = arr != null && arr.Length > 0 ? arr[0] : viewpoint!;
            viewpoint = targetViewpoint;
        }

        // step into mapping
        var viewpointMap = PronounMapping[viewpoint!];
        if (!viewpointMap.TryGetValue(pronounType!, out var pronouns))
            pronouns = viewpointMap[DefaultPronounType];
        if (!pronouns.TryGetValue(gender!, out var mapped))
            mapped = pronouns[DefaultGender];

        var mappedPronoun = mapped;
        if (pronoun != "I")
            mappedPronoun = CopyWordCase(pronoun, mappedPronoun);
        if (mappedPronoun == "i") mappedPronoun = mappedPronoun.ToUpperInvariant();

        if (viewpoint == "3rd person")
            return (pronoun, mappedPronoun);
        else
            return (mappedPronoun, pronoun);
    }

    // Overload that mirrors python's signature: pronoun_to_viewpoints(pronoun, options, pronoun_type=..., gender=..., viewpoint=...)
    public static (string, string) PronounToViewpoints(string pronoun, IEnumerable<string>? options, string? pronounType = null, string? gender = null, string? viewpoint = null)
        => PronounToViewpoints(pronoun, (object?)options, pronounType, gender, viewpoint);
}
