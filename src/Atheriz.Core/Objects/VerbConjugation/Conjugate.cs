// Port of atheriz/objects/verb_conjugation/conjugate.py:1
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Atheriz.Core.Objects.VerbConjugation;

/// <summary>
/// Port of <c>atheriz/objects/verb_conjugation/conjugate.py</c> (413 LOC).
/// Evennia-derived irregular table preserved via embedded <c>verbs.txt</c> subset.
/// Covers be/have/do/go plus generic fallback (+s) for unknown verbs.
/// </summary>
public static class Conjugate
{
    // verb_tenses_keys: index mapping (negated forms are ind+12 in full file)
    private static readonly Dictionary<string, int> VerbTensesKeys = new(StringComparer.Ordinal)
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
    };

    private static readonly Dictionary<string, string> VerbTensesAliases = new(StringComparer.Ordinal)
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
    };

    // verb_tenses: infinitive -> array (0..11 positive, 12..23 negated where present). Mirrors Python verbs.txt loading.
    private static readonly Dictionary<string, string[]> VerbTenses;
    private static readonly Dictionary<string, string> VerbLemmas;

    static Conjugate()
    {
        var raw = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        bool loadedFromFile = false;
        // Try to load the real verbs.txt like Python does, for full fidelity (covers swim, negated, etc)
        var candidatePaths = new[]
        {
            "/home/anon/atheriz/atheriz/objects/verb_conjugation/verbs.txt",
            "atheriz/objects/verb_conjugation/verbs.txt",
            Path.Combine(AppContext.BaseDirectory, "verbs.txt"),
        };
        string? found = candidatePaths.FirstOrDefault(File.Exists);
        if (found != null)
        {
            try
            {
                foreach (var line in File.ReadAllLines(found))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length == 0 || string.IsNullOrEmpty(parts[0])) continue;
                    // Keep at least 12 cols; keep all cols (negated included) for verb_conjugate negate handling
                    // Ensure length at least 12, but keep whatever file provides (up to 24)
                    raw[parts[0]] = parts;
                }
                if (raw.Count > 100) loadedFromFile = true;
            }
            catch { raw.Clear(); }
        }
        if (!loadedFromFile)
        {
            // Fallback embedded subset (as before) if file not found
            raw = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["be"] = new[] { "be","am","are","is","are","being","was","were","was","were","were","been","","am not","aren't","isn't","aren't","","wasn't","weren't","wasn't","weren't","weren't" },
                ["have"] = new[] { "have","","","has","","having","","","","","had","had","haven't","","hasn't","","","","","hadn't","hadn't" },
                ["do"] = new[] { "do","","","does","","doing","","","","","did","done","don't","","doesn't","","","","","didn't" },
                ["go"] = new[] { "go","","","goes","","going","","","","","went","gone" },
                ["say"] = new[] { "say","","","says","","saying","","","","","said","said" },
                ["swim"] = new[] { "swim","","","swims","","swimming","","","","","swam","swum" },
            };
            string[] RegularRow(string baseVerb)
            {
                var third = baseVerb.EndsWith("s") || baseVerb.EndsWith("x") || baseVerb.EndsWith("z") || baseVerb.EndsWith("ch") || baseVerb.EndsWith("sh") ? baseVerb + "es" : baseVerb + "s";
                if (baseVerb.EndsWith("y") && baseVerb.Length > 1 && !"aeiou".Contains(char.ToLower(baseVerb[^2])))
                    third = baseVerb[..^1] + "ies";
                var prog = baseVerb.EndsWith("e") ? baseVerb[..^1] + "ing" : baseVerb + "ing";
                if (baseVerb.EndsWith("e") && baseVerb.EndsWith("ie")) prog = baseVerb[..^2] + "ying";
                var past = baseVerb.EndsWith("e") ? baseVerb + "d" : baseVerb + "ed";
                if (baseVerb.EndsWith("y") && !"aeiou".Contains(char.ToLower(baseVerb[^2]))) past = baseVerb[..^1] + "ied";
                return new[] { baseVerb, "", "", third, "", prog, "", "", "", "", past, past };
            }
            foreach (var v in new[] { "jump","attack","walk","look","get","put","give","take","run","eat","see","make","come","know","want","need","help","open","close","lock","unlock","smile","grin","laugh","bow","nod","wave","dance","sing","shout","whisper","ask","answer","follow","wander","drop","hold","carry","throw","catch","hit","kick","slay","kill","hug","kiss","poke","push","pull","turn","leave","enter","move","cry","try","fly","swim" })
            {
                if (!raw.ContainsKey(v))
                    raw[v] = RegularRow(v);
            }
            raw["run"] = new[] { "run","","","runs","","running","","","","","ran","run" };
            raw["eat"] = new[] { "eat","","","eats","","eating","","","","","ate","eaten" };
            raw["see"] = new[] { "see","","","sees","","seeing","","","","","saw","seen" };
            raw["make"] = new[] { "make","","","makes","","making","","","","","made","made" };
            raw["come"] = new[] { "come","","","comes","","coming","","","","","came","come" };
            raw["know"] = new[] { "know","","","knows","","knowing","","","","","knew","known" };
            raw["take"] = new[] { "take","","","takes","","taking","","","","","took","taken" };
            raw["give"] = new[] { "give","","","gives","","giving","","","","","gave","given" };
            raw["hit"] = new[] { "hit","","","hits","","hitting","","","","","hit","hit" };
            raw["put"] = new[] { "put","","","puts","","putting","","","","","put","put" };
            if (!raw.ContainsKey("swim")) raw["swim"] = new[] { "swim","","","swims","","swimming","","","","","swam","swum" };
        }
        else
        {
            // Ensure even when loaded, we have at least expected regular fallback for unknown test verbs if not in file?
            // Most verbs are in file, but keep as is.
        }

        VerbTenses = raw;

        // Build lemmas: each inflected form -> infinitive (including negated forms, but they map same)
        VerbLemmas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in VerbTenses)
        {
            var infinitive = kv.Key;
            foreach (var form in kv.Value)
            {
                if (!string.IsNullOrEmpty(form))
                    VerbLemmas[form] = infinitive;
            }
            VerbLemmas[infinitive] = infinitive;
        }
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
        try
        {
            if (!VerbTenses.TryGetValue(verb, out var row)) return verb;
            if (ind >= row.Length) return verb;
            var val = row[ind];
            // Python returns "" for empty entry (caller checks != ""), not verb
            return val ?? "";
        }
        catch { return verb; }
    }

    public static string VerbPresent(string verb, string person = "", bool negate = false)
    {
        person = NormalizePerson(person);
        var mapping = new Dictionary<string,string>
        {
            ["1"] = "1st singular present",
            ["2"] = "2nd singular present",
            ["3"] = "3rd singular present",
            ["*"] = "present plural",
        };
        if (mapping.TryGetValue(person, out var tense))
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
        var mapping = new Dictionary<string,string>
        {
            ["1"] = "1st singular past",
            ["2"] = "2nd singular past",
            ["3"] = "3rd singular past",
            ["*"] = "past plural",
        };
        if (mapping.TryGetValue(person, out var tense))
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
        if (!VerbTenses.TryGetValue(infinitive, out var data)) return infinitive;
        foreach (var kv in VerbTensesKeys)
        {
            var tense = kv.Key;
            var idx = kv.Value;
            if (idx < data.Length && data[idx] == verb) return tense;
            if (idx + VerbTensesKeys.Count < data.Length && data[idx + VerbTensesKeys.Count] == verb) return tense;
        }
        if (string.Equals(infinitive, verb, StringComparison.OrdinalIgnoreCase)) return "infinitive";
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
        var mapping = new Dictionary<string,string>
        {
            ["1"] = "1st singular present",
            ["2"] = "2nd singular present",
            ["3"] = "3rd singular present",
            ["*"] = "present plural",
        };
        var infinitive = VerbInfinitive(verb);
        if (personNorm == "")
        {
            foreach (var tense in mapping.Values)
            {
                if (verb == VerbConjugate(infinitive, tense, negate: negated)) return true;
            }
            return false;
        }
        if (mapping.TryGetValue(personNorm, out var target))
        {
            var expected = VerbConjugate(infinitive, target, negate: negated);
            if (string.IsNullOrEmpty(expected)) return false;
            return verb == expected;
        }
        return false;
    }

    public static bool VerbIsPast(string verb, string person = "", bool negated = false)
    {
        var personNorm = NormalizePerson(person);
        var mapping = new Dictionary<string,string>
        {
            ["1"] = "1st singular past",
            ["2"] = "2nd singular past",
            ["3"] = "3rd singular past",
            ["*"] = "past plural",
        };
        var infinitive = VerbInfinitive(verb);
        if (personNorm == "")
        {
            foreach (var tense in mapping.Values.Concat(new[] { "past" }))
            {
                if (verb == VerbConjugate(infinitive, tense, negate: negated)) return true;
            }
            return false;
        }
        if (mapping.TryGetValue(personNorm, out var target))
        {
            var expected = VerbConjugate(infinitive, target, negate: negated);
            if (!string.IsNullOrEmpty(expected)) return verb == expected;
            return verb == VerbConjugate(infinitive, "past", negate: negated);
        }
        return false;
    }

    public static bool VerbIsPresentParticiple(string verb) => VerbTense(verb) == "present participle";
    public static bool VerbIsPastParticiple(string verb) => VerbTense(verb) == "past participle";

    private static string NormalizePerson(string person)
    {
        if (person == null) return "";
        var s = person.ToString()!.Replace("pl", "*").Trim();
        // strip "stndrgural" as python does: strip chars s,t,n,d,r,g,u,a,l
        // python: .strip("stndrgural") removes those chars from both ends.
        s = s.Trim('s','t','n','d','r','g','u','a','l');
        // also need to handle "*" preservation
        if (s == "*") return "*";
        // extract digit if present
        foreach (var c in s) if (char.IsDigit(c)) return c.ToString();
        if (s == "*") return "*";
        return s;
    }

    /// <summary>
    /// Port of <c>verb_actor_stance_components</c>. Returns (2nd, 3rd) forms.
    /// </summary>
    public static (string second, string third) VerbActorStanceComponents(string verb, bool plural = false)
    {
        var tense = VerbTense(verb);
        if (tense == null) return (verb, verb);
        var them = plural ? "*" : "3";
        var themSuff = plural ? "" : "s";

        if (tense.Contains("participle") || tense.Contains("plural"))
            return (verb, verb);
        if (tense == "infinitive" || tense.Contains("present"))
        {
            var youStr = VerbPresent(verb, "2");
            if (string.IsNullOrEmpty(youStr)) youStr = verb;
            var themStr = VerbPresent(verb, them);
            if (string.IsNullOrEmpty(themStr)) themStr = verb + themSuff;
            // fallback for generic unknown where VerbPresent returns infinitive unchanged but we still want +s for third
            if (!plural && themStr == verb && !string.Equals(youStr, verb, StringComparison.OrdinalIgnoreCase))
            {
                // if verb is base infinitive and themStr equals verb, add s
                // Check if verb is infinitive form; then third should be +s
                if (VerbInfinitive(verb) == verb) themStr = verb + themSuff;
            }
            return (youStr, themStr);
        }
        else
        {
            var youStr = VerbPast(verb, "2");
            if (string.IsNullOrEmpty(youStr)) youStr = verb;
            var themStr = VerbPast(verb, them);
            if (string.IsNullOrEmpty(themStr)) themStr = verb + themSuff;
            return (youStr, themStr);
        }
    }
}
