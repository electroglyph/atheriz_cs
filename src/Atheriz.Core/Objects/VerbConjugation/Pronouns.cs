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

    // O(1) membership matching today's linear scans: the array Contains calls
    // below use default (ordinal, case-sensitive) equality, so these sets must
    // use Ordinal too — any other comparer would silently change matches.
    private static readonly HashSet<string> PronounTypeSet = new(PronounTypes, StringComparer.Ordinal);
    private static readonly HashSet<string> ViewpointSet = new(Viewpoints, StringComparer.Ordinal);
    private static readonly HashSet<string> GenderSet = new(Genders, StringComparer.Ordinal);

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

    // PRONOUN_TABLE: pronoun lower -> (viewpoint, gender(s), pronoun_type(s)).
    // Typed string arrays (no boxing, no IsIter flag): single-attribute
    // pronouns hold one-element arrays.
    public static readonly Dictionary<string, (string Viewpoint, string[] Genders, string[] Types)> PronounTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["I"] = ("1st person", ["neutral", "male", "female", "plural"], ["subject pronoun"]),
            ["me"] = ("1st person", ["neutral", "male", "female", "plural"], ["object pronoun"]),
            ["my"] = ("1st person", ["neutral", "male", "female", "plural"], ["possessive adjective"]),
            ["mine"] = ("1st person", ["neutral", "male", "female", "plural"], ["possessive pronoun"]),
            ["myself"] = ("1st person", ["neutral", "male", "female", "plural"], ["reflexive pronoun"]),
            ["we"] = ("1st person", ["plural"], ["subject pronoun"]),
            ["us"] = ("1st person", ["plural"], ["object pronoun"]),
            ["our"] = ("1st person", ["plural"], ["possessive adjective"]),
            ["ours"] = ("1st person", ["plural"], ["possessive pronoun"]),
            ["ourselves"] = ("1st person", ["plural"], ["reflexive pronoun"]),
            ["you"] = ("2nd person", ["neutral", "male", "female", "plural"], ["subject pronoun", "object pronoun"]),
            ["your"] = ("2nd person", ["neutral", "male", "female", "plural"], ["possessive adjective"]),
            ["yours"] = ("2nd person", ["neutral", "male", "female", "plural"], ["possessive pronoun"]),
            ["yourself"] = ("2nd person", ["neutral", "male", "female"], ["reflexive pronoun"]),
            ["yourselves"] = ("2nd person", ["plural"], ["reflexive pronoun"]),
            ["he"] = ("3rd person", ["male"], ["subject pronoun"]),
            ["him"] = ("3rd person", ["male"], ["object pronoun"]),
            ["his"] = ("3rd person", ["male"], ["possessive pronoun", "possessive adjective"]),
            ["himself"] = ("3rd person", ["male"], ["reflexive pronoun"]),
            ["she"] = ("3rd person", ["female"], ["subject pronoun"]),
            ["her"] = ("3rd person", ["female"], ["object pronoun", "possessive adjective"]),
            ["hers"] = ("3rd person", ["female"], ["possessive pronoun"]),
            ["herself"] = ("3rd person", ["female"], ["reflexive pronoun"]),
            ["it"] = ("3rd person", ["neutral"], ["subject pronoun", "object pronoun"]),
            ["its"] = ("3rd person", ["neutral"], ["possessive pronoun", "possessive adjective"]),
            ["itself"] = ("3rd person", ["neutral"], ["reflexive pronoun"]),
            ["they"] = ("3rd person", ["plural"], ["subject pronoun"]),
            ["them"] = ("3rd person", ["plural"], ["object pronoun"]),
            ["their"] = ("3rd person", ["plural"], ["possessive adjective"]),
            ["theirs"] = ("3rd person", ["plural"], ["possessive pronoun"]),
            ["themselves"] = ("3rd person", ["plural"], ["reflexive pronoun"]),
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
        if (!PronounTypeSet.Contains(pronounType ?? ""))
        {
            pronounType = sourceType.Length > 0 ? sourceType[0] : DefaultPronounType;
        }
        if (!ViewpointSet.Contains(viewpoint ?? ""))
        {
            viewpoint = sourceViewpoint;
        }
        if (!GenderSet.Contains(gender ?? ""))
        {
            gender = sourceGender.Length > 0 ? sourceGender[0] : DefaultGender;
        }

        if (options is not null)
        {
            // Single pass: normalize (trim, lower, alias) and classify each
            // option immediately, in order — same decisions as the old
            // collect-then-normalize-then-classify chain.
            IEnumerable<string> raw;
            if (options is string sopt)
                raw = sopt.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            else if (options is IEnumerable<string> es)
                raw = es;
            else if (options is IEnumerable<object> eo)
                raw = eo.Select(o => o?.ToString() ?? "");
            else
                raw = [options.ToString() ?? ""];

            foreach (var item in raw)
            {
                var opt = item.Trim().ToLowerInvariant();
                if (Aliases.TryGetValue(opt, out var a)) opt = a;
                if (PronounTypeSet.Contains(opt)) pronounType = opt;
                else if (ViewpointSet.Contains(opt)) viewpoint = opt;
                else if (GenderSet.Contains(opt)) gender = opt;
            }
        }

        // validate sourceType handling: multi-attribute sources narrow to the
        // requested attribute when it is one of theirs, else fall back to the
        // first. (Length > 1 reproduces the old IsIter split: every
        // multi-element table entry was an array, every single was a string.)
        if (sourceType.Length > 1)
        {
            if (!sourceType.Contains(pronounType!)) pronounType = sourceType[0];
        }
        else
        {
            // A single-typed source keeps its own type, unless the caller explicitly
            // requested a different valid type (parameter or options): defaulting
            // above can only reproduce the source type here, so any other valid
            // value is an explicit request and must survive.
            var single = sourceType.Length > 0 ? sourceType[0] : DefaultPronounType;
            if (!PronounTypeSet.Contains(pronounType ?? "") || pronounType == single)
                pronounType = single;
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
            if (arr is not null && arr.Contains(viewpoint!)) targetViewpoint = viewpoint!;
            else targetViewpoint = arr?.FirstOrDefault() ?? viewpoint!;
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
            mappedPronoun = GameUtils.CopyWordCase(pronoun, mappedPronoun);
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
