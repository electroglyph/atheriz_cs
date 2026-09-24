using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Atheriz.Core.Objects.VerbConjugation;

/// <summary>
/// Evennia-derived irregular table preserved via embedded <c>verbs.txt</c> subset.
/// Covers be/have/do/go plus generic fallback (+s) for unknown verbs.
/// </summary>
public static class Conjugate
{
    // verb_tenses_keys: index mapping (negated forms are ind+12 in full file).
    // Frozen lookup tables: read-only after load, hashed once.
    private static readonly FrozenDictionary<string, int> VerbTensesKeys =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["infinitive"] = 0,
        ["1st singular present"] = 1,
        ["2nd singular present"] = 2,
        ["3rd singular present"] = 3,
        ["present plural"] = 4,
        ["present participle"] = 5,
        ["1st singular past"] = 6,
        ["2nd singular past"] = 7,
        ["3rd singular past"] = 8,
        ["past plural"] = 9,
        ["past"] = 10,
        ["past participle"] = 11,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> VerbTensesAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["inf"] = "infinitive",
        ["1sgpres"] = "1st singular present",
        ["2sgpres"] = "2nd singular present",
        ["3sgpres"] = "3rd singular present",
        ["pl"] = "present plural",
        ["prog"] = "present participle",
        ["1sgpast"] = "1st singular past",
        ["2sgpast"] = "2nd singular past",
        ["3sgpast"] = "3rd singular past",
        ["pastpl"] = "past plural",
        ["ppart"] = "past participle",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // verb_tenses: infinitive -> array (0..11 positive, 12..23 negated where present). Mirrors Python verbs.txt loading.
    private static readonly FrozenDictionary<string, string[]> VerbTenses;
    private static readonly FrozenDictionary<string, string> VerbLemmas;

    static Conjugate()
    {
        var raw = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void ParseLines(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length == 0 || string.IsNullOrEmpty(parts[0])) continue;
                // Keep at least 12 cols; keep all cols (negated included) for verb_conjugate negate handling
                // Ensure length at least 12, but keep whatever file provides (up to 24)
                raw[parts[0]] = parts;
            }
        }
        static IEnumerable<string> ReadLines(StreamReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) is not null) yield return line;
        }
        // The module table ships embedded in the assembly (the equivalent of
        // Python's os.path.dirname(__file__)/verbs.txt) and always wins, so
        // identical builds load the identical table whatever the launch
        // directory holds. (Resource-blob read only — no member reflection.)
        try
        {
            using var stream = typeof(Conjugate).Assembly.GetManifestResourceStream("Atheriz.Core.Objects.VerbConjugation.verbs.txt");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                ParseLines(ReadLines(reader));
            }
        }
        catch { raw.Clear(); }
        // No further fallbacks: the table ships embedded (EmbeddedResource
        // only — no output-dir copy to shadow it) and always wins above. If
        // the resource is ever missing the table stays empty and every
        // lookup below passes the verb through unchanged (pinned by
        // VerbInfinitiveUnknown), instead of a stale hardcoded subset.
        // Build lemmas: each inflected form -> infinitive (including negated forms, but they map same).
        // Last-wins on collisions is VERBATIM Python (conjugate.py:73-77 unconditional assignment) — keep.
        // Built from insertion-ordered `raw` BEFORE freezing: frozen enumeration
        // order is unspecified, so freezing first would reshuffle collisions.
        var lemmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in raw)
        {
            var infinitive = kv.Key;
            foreach (var form in kv.Value)
            {
                if (!string.IsNullOrEmpty(form))
                    lemmas[form] = infinitive;
            }
            lemmas[infinitive] = infinitive;
        }
        VerbTenses = raw.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        VerbLemmas = lemmas.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static string VerbInfinitive(string verb)
    {
        if (string.IsNullOrEmpty(verb)) return verb;
        return VerbLemmas.TryGetValue(verb, out var inf) ? inf : verb;
    }

    public static string VerbConjugate(string verb, string tense = "infinitive", bool negate = false)
    {
        if (VerbTensesAliases.TryGetValue(tense, out var aliased)) tense = aliased;
        verb = VerbInfinitive(verb);
        if (!VerbTensesKeys.TryGetValue(tense, out var ind)) return verb;
        if (negate) ind += VerbTensesKeys.Count;
        if (!VerbTenses.TryGetValue(verb, out var row)) return verb;
        if (ind >= row.Length) return verb;
        var val = row[ind];
        // Python returns "" for empty entry (caller checks != ""), not verb
        return val ?? "";
    }

    private static readonly FrozenDictionary<string, string> PresentPersonTenses =
        new Dictionary<string, string>
        {
            ["1"] = "1st singular present",
            ["2"] = "2nd singular present",
            ["3"] = "3rd singular present",
            ["*"] = "present plural",
        }.ToFrozenDictionary();
    private static readonly FrozenDictionary<string, string> PastPersonTenses =
        new Dictionary<string, string>
        {
            ["1"] = "1st singular past",
            ["2"] = "2nd singular past",
            ["3"] = "3rd singular past",
            ["*"] = "past plural",
        }.ToFrozenDictionary();
    private static bool MatchesAnyTense(string verb, string infinitive, IEnumerable<string> tenses, bool negated)
    {
        foreach (var tense in tenses)
            if (string.Equals(verb, VerbConjugate(infinitive, tense, negate: negated), StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static string VerbPresent(string verb, string person = "", bool negate = false)
    {
        person = NormalizePerson(person);
        if (PresentPersonTenses.TryGetValue(person, out var tense))
        {
            var c = VerbConjugate(verb, tense, negate);
            if (!string.IsNullOrEmpty(c)) return c;
        }
        var inf = VerbConjugate(verb, "infinitive", negate);
        return string.IsNullOrEmpty(inf) ? verb : inf;
    }

    public static string VerbPresentParticiple(string verb) => VerbConjugate(verb, "present participle");

    public static string VerbPast(string verb, string person = "", bool negate = false)
    {
        person = NormalizePerson(person);
        if (PastPersonTenses.TryGetValue(person, out var tense))
        {
            var c = VerbConjugate(verb, tense, negate);
            if (!string.IsNullOrEmpty(c)) return c;
        }
        var past = VerbConjugate(verb, "past", negate: negate);
        return string.IsNullOrEmpty(past) ? verb : past;
    }

    public static string VerbPastParticiple(string verb) => VerbConjugate(verb, "past participle");

    public static List<string> VerbAllTenses() => new(VerbTensesKeys.Keys);

    public static string? VerbTense(string verb)
    {
        var infinitive = VerbInfinitive(verb);
// unknown verbs have no tense data,
        // so None is returned (pinned by test_verb_conjugate.py:203).
        if (!VerbTenses.TryGetValue(infinitive, out var data)) return null;
        foreach (var kv in VerbTensesKeys)
        {
            var tense = kv.Key;
            var idx = kv.Value;
            if (idx < data.Length && string.Equals(data[idx], verb, StringComparison.OrdinalIgnoreCase)) return tense;
            if (idx + VerbTensesKeys.Count < data.Length && string.Equals(data[idx + VerbTensesKeys.Count], verb, StringComparison.OrdinalIgnoreCase)) return tense;
        }
        if (string.Equals(infinitive, verb, StringComparison.OrdinalIgnoreCase)) return "infinitive";
        // No table form matched (conjugate.py:263-267 falls off the end).
        return null;
    }

    public static bool VerbIsTense(string verb, string tense)
    {
        if (VerbTensesAliases.TryGetValue(tense, out var a)) tense = a;
        return VerbTense(verb) == tense;
    }

    public static bool VerbIsPresent(string verb, string person = "", bool negated = false)
    {
        var personNorm = NormalizePerson(person);
        var infinitive = VerbInfinitive(verb);
        if (personNorm == "")
        {
            return MatchesAnyTense(verb, infinitive, PresentPersonTenses.Values, negated);
        }
        if (PresentPersonTenses.TryGetValue(personNorm, out var target))
        {
            var expected = VerbConjugate(infinitive, target, negate: negated);
            if (string.IsNullOrEmpty(expected)) return false;
            return string.Equals(verb, expected, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static bool VerbIsPast(string verb, string person = "", bool negated = false)
    {
        var personNorm = NormalizePerson(person);
        var infinitive = VerbInfinitive(verb);
        if (personNorm == "")
        {
            return MatchesAnyTense(verb, infinitive, PastPersonTenses.Values.Append("past"), negated);
        }
        if (PastPersonTenses.TryGetValue(personNorm, out var target))
        {
            var expected = VerbConjugate(infinitive, target, negate: negated);
            if (!string.IsNullOrEmpty(expected)) return string.Equals(verb, expected, StringComparison.OrdinalIgnoreCase);
            return string.Equals(verb, VerbConjugate(infinitive, "past", negate: negated), StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static bool VerbIsPresentParticiple(string verb) => VerbTense(verb) == "present participle";
    public static bool VerbIsPastParticiple(string verb) => VerbTense(verb) == "past participle";

    // Person normalization: free text to a person key ("1"/"2"/"3"/"*"/"").
    // Explicit alias map — the old pipeline stripped real letters
    // ("past"→"p") and dug digits out of anywhere ("2nd person"→"2").
    // Unknown text falls back to "" (the any-tense/default path), the same
    // outcome every previously-missing key already produced.
    private static readonly FrozenDictionary<string, string> PersonAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = "1", ["1st"] = "1", ["1sg"] = "1", ["first"] = "1",
            ["2"] = "2", ["2nd"] = "2", ["2sg"] = "2", ["second"] = "2",
            ["3"] = "3", ["3rd"] = "3", ["3sg"] = "3", ["third"] = "3",
            ["*"] = "*", ["pl"] = "*", ["plural"] = "*",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static string NormalizePerson(string person)
    {
        if (string.IsNullOrWhiteSpace(person)) return "";
        return PersonAliases.TryGetValue(person.Trim(), out var canon) ? canon : "";
    }

    /// <summary>
    /// </summary>
    public static (string second, string third) VerbActorStanceComponents(string verb, bool plural = false)
    {
        var tense = VerbTense(verb);
        // unchanged for both persons ("he florp", not "he florps").
        if (tense is null) return (verb, verb);
        var them = plural ? "*" : "3";

        if (tense.Contains("participle") || tense.Contains("plural"))
            return (verb, verb);
        if (tense == "infinitive" || tense.Contains("present"))
        {
            // VerbPresent never returns empty (infinitive fallback), so no
            // empty-guard is needed on either form.
            return (VerbPresent(verb, "2"), VerbPresent(verb, them));
        }
        else
        {
            // Same contract on the past path: VerbPast falls back to the verb.
            return (VerbPast(verb, "2"), VerbPast(verb, them));
        }
    }
}
