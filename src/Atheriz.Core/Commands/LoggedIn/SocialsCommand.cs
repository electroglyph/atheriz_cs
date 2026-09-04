// Port of atheriz/commands/loggedin/socials.py:131
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class SocialsCommand : Command
{
    public override string Key => "socials";
    public override IReadOnlyList<string> Aliases => SocialsDict.Keys.ToList();
    public override string Desc => "Social commands. Use 'help socials' for list.";
    public override string Category => "Socials";
    public static readonly Dictionary<string, (string self, string target)> SocialsDict = new(StringComparer.OrdinalIgnoreCase)
    {
        ["smile"] = ("$You() $conj(smile).", "$You() $conj(smile) at $you(target)."),
        ["frown"] = ("$You() $conj(frown) disapprovingly.", "$You() $conj(frown) disapprovingly at $you(target)."),
        ["grimace"] = ("$You() $conj(grimace) in pain.", "$You() $conj(grimace) in pain at $you(target)."),
        ["smirk"] = ("$You() $conj(smirk).", "$You() $conj(smirk) at $you(target)."),
        ["pout"] = ("$You() $conj(pout) childishly.", "$You() $conj(pout) childishly at $you(target)."),
        ["cringe"] = ("$You() $conj(cringe) in embarrassment.", "$You() $conj(cringe) in embarrassment at $you(target)."),
        ["glare"] = ("$You() $conj(glare) angrily.", "$You() $conj(glare) angrily at $you(target)."),
        ["stare"] = ("$You() $conj(stare) blankly into space.", "$You() $conj(stare) blankly at $you(target)."),
        ["nod"] = ("$You() $conj(nod) in agreement.", "$You() $conj(nod) in agreement at $you(target)."),
        ["shrug"] = ("$You() $conj(shrug) helplessly.", "$You() $conj(shrug) helplessly at $you(target)."),
        ["bow"] = ("$You() $conj(bow) gracefully.", "$You() $conj(bow) gracefully to $you(target)."),
        ["wave"] = ("$You() $conj(wave) happily.", "$You() $conj(wave) happily to $you(target)."),
        ["salute"] = ("$You() $conj(salute) respectfully.", "$You() $conj(salute) respectfully to $you(target)."),
        ["point"] = ("$You() $conj(point) out something interesting.", "$You() $conj(point) at $you(target)."),
        ["roll"] = ("$You() $conj(roll) $pron(your) eyes.", "$You() $conj(roll) $pron(your) eyes at $you(target)."),
        ["blink"] = ("$You() $conj(blink) in confusion.", "$You() $conj(blink) in confusion at $you(target)."),
        ["wink"] = ("$You() $conj(wink) knowingly.", "$You() $conj(wink) knowingly at $you(target)."),
        ["dance"] = ("$You() $conj(dance) around.", "$You() $conj(dance) around with $you(target)."),
        ["bounce"] = ("$You() $conj(bounce) around happily.", "$You() $conj(bounce) around happily with $you(target)."),
        ["stretch"] = ("$You() $conj(stretch) $pron(your) muscles.", "$You() $conj(stretch) $pron(your) muscles in front of $you(target)."),
        ["shiver"] = ("$You() $conj(shiver) from the cold.", "$You() $conj(shiver) from the cold next to $you(target)."),
        ["tap"] = ("$You() $conj(tap) $pron(your) foot impatiently.", "$You() $conj(tap) $pron(your) foot impatiently at $you(target)."),
        ["scratch"] = ("$You() $conj(scratch) $pron(your) head in confusion.", "$You() $conj(scratch) $pron(your) head in confusion at $you(target)."),
        ["snap"] = ("$You() $conj(snap) $pron(your) fingers.", "$You() $conj(snap) $pron(your) fingers at $you(target)."),
        ["laugh"] = ("$You() $conj(laugh) out loud.", "$You() $conj(laugh) out loud at $you(target)."),
        ["chuckle"] = ("$You() $conj(chuckle) quietly.", "$You() $conj(chuckle) quietly at $you(target)."),
        ["giggle"] = ("$You() $conj(giggle) softly.", "$You() $conj(giggle) softly at $you(target)."),
        ["snicker"] = ("$You() $conj(snicker) mischievously.", "$You() $conj(snicker) mischievously at $you(target)."),
        ["sigh"] = ("$You() $conj(sigh) deeply.", "$You() $conj(sigh) deeply at $you(target)."),
        ["yawn"] = ("$You() $conj(yawn) tiredly.", "$You() $conj(yawn) tiredly at $you(target)."),
        ["groan"] = ("$You() $conj(groan) loudly.", "$You() $conj(groan) loudly at $you(target)."),
        ["grumble"] = ("$You() $conj(grumble) under $pron(your) breath.", "$You() $conj(grumble) under $pron(your) breath at $you(target)."),
        ["growl"] = ("$You() $conj(growl) menacingly.", "$You() $conj(growl) menacingly at $you(target)."),
        ["cry"] = ("$You() $conj(cry) softly.", "$You() $conj(cry) softly on $you(target)'s shoulder."),
        ["weep"] = ("$You() $conj(weep) uncontrollably.", "$You() $conj(weep) uncontrollably in front of $you(target)."),
        ["gasp"] = ("$You() $conj(gasp) in astonishment!", "$You() $conj(gasp) in astonishment at $you(target)!"),
        ["whistle"] = ("$You() $conj(whistle) a happy tune.", "$You() $conj(whistle) appreciatively at $you(target)."),
        ["ponder"] = ("$You() $conj(ponder) the situation.", "$You() $conj(ponder) $you(target)."),
        ["boggle"] = ("$You() $conj(boggle) at the concept.", "$You() $conj(boggle) at $you(target)."),
        ["facepalm"] = ("$You() $conj(facepalm).", "$You() $conj(facepalm) at $you(target)."),
        ["slap"] = ("$You() $conj(slap) $pron(your) forehead.", "$You() $conj(slap) $you(target)."),
        ["poke"] = ("$You() $conj(poke) the air playfully.", "$You() $conj(poke) $you(target) playfully."),
        ["hug"] = ("$You() $conj(hug) $pron(yourself).", "$You() $conj(hug) $you(target)."),
        ["applaud"] = ("$You() $conj(applaud) enthusiastically.", "$You() $conj(applaud) $you(target) enthusiastically."),
        ["clap"] = ("$You() $conj(clap) $pron(your) hands.", "$You() $conj(clap) for $you(target)."),
        ["cheer"] = ("$You() $conj(cheer) loudly!", "$You() $conj(cheer) loudly for $you(target)!"),
        ["cough"] = ("$You() $conj(cough) uncomfortably.", "$You() $conj(cough) uncomfortably at $you(target)."),
        ["sneeze"] = ("$You() $conj(sneeze) loudly.", "$You() $conj(sneeze) loudly on $you(target)."),
        ["sniff"] = ("$You() $conj(sniff) the air.", "$You() $conj(sniff) $you(target)."),
        ["burp"] = ("$You() $conj(burp) rudely.", "$You() $conj(burp) rudely at $you(target)."),
        ["spit"] = ("$You() $conj(spit) on the ground.", "$You() $conj(spit) on $you(target)!"),
        ["sulk"] = ("$You() $conj(sulk) quietly in the corner.", "$You() $conj(sulk) because of $you(target)."),
    };
    public override string PrintHelp()
    {
        string baseHelp = Parser?.FormatHelp() ?? "";
        return $"{baseHelp}\nAvailable socials:\n{string.Join(", ", Aliases)}\n";
    }
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("target", nargs: "*", help: "Who or what to do this to."); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        string verb = pa?.CmdString ?? Key;
        if (!SocialsDict.TryGetValue(verb, out var templates))
        {
            go.Msg("This command is meant to be invoked via one of its aliases: " + string.Join(", ", Aliases));
            return;
        }
        var targetNames = pa?.GetList("target") ?? [];
        string targetName = string.Join(" ", targetNames);
        var loc = go.ResolveLocationObject() as Node;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            string msg = templates.self;
            if (loc != null) loc.MsgContents(msg, fromObj: go, mapping: new Dictionary<string, object?> { ["you"] = go });
            else go.MsgContents(msg, fromObj: go, mapping: new Dictionary<string, object?> { ["you"] = go });
        }
        else
        {
            List<GameObject> targets;
            try { targets = go.Search(targetName, true, go); } catch { targets = []; }
            if (targets.Count == 0 && loc != null) try { targets = loc.Search(targetName, true, go); } catch { }
            if (targets.Count == 0) { go.Msg($"Could not find '{targetName}'."); return; }
            if (targets.Count > 1) { go.Msg($"Multiple matches for '{targetName}'. Be more specific."); return; }
            var target = targets[0];
            string msg = templates.target;
            if (loc != null) loc.MsgContents(msg, fromObj: go, mapping: new Dictionary<string, object?> { ["you"] = go, ["target"] = target });
            else go.MsgContents(msg, fromObj: go, mapping: new Dictionary<string, object?> { ["you"] = go, ["target"] = target });
        }
    }
}
