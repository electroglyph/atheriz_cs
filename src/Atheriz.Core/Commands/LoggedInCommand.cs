using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands;

/// <summary>
/// Base for logged-in (puppet) commands. Overrides
/// <see cref="Command.Run(CommandContext)"/> to resolve the puppet once via
/// <see cref="CommandHelpers.RequirePuppet"/> (the two-line preamble
/// previously pasted into ~40 <c>Run</c> methods) and to split parsed vs raw
/// input, then dispatches to <see cref="RunPuppet"/> /
/// <see cref="RunPuppetRaw"/>.
/// Missing/non-parsed args send <see cref="Command.PrintHelp"/> exactly like
/// <see cref="CommandHelpers.RequireParsedArgs"/> did, unless
/// <see cref="AllowMissingArgs"/> opts out (e.g. examine-here).
/// </summary>
public abstract class LoggedInCommand : Command
{
    /// <summary>Deny message when the caller is not a puppeted character.</summary>
    protected virtual string? DenyMessage => "You can't do that.";

    /// <summary>
    /// True when a null/non-parsed <c>args</c> means "no arguments" (empty
    /// bag) rather than "show help". Only for verbs where the old body was
    /// null-tolerant (e.g. <c>examine</c> with no target examines here).
    /// </summary>
    protected virtual bool AllowMissingArgs => false;

    public override void Run(CommandContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!CommandHelpers.RequirePuppet(ctx.Caller, out var go, DenyMessage)) return;
        if (UseParser)
        {
            var pa = ctx.Args;
            if (pa is null && !AllowMissingArgs) { go.Msg(PrintHelp()); return; }
            RunParsed(ctx with { Puppet = go, Args = pa ?? new GameArgumentParser.ParsedArgs() });
        }
        else
        {
            RunRaw(ctx with { Puppet = go });
        }
    }

    public override void RunParsed(CommandContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Puppet is not GameObject go || ctx.Args is null) { ctx.Caller.Msg(PrintHelp()); return; }
        RunPuppet(go, ctx.Args, ctx.Ct);
    }

    public override void RunRaw(CommandContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Puppet is not GameObject go) { ctx.Caller.Msg(PrintHelp()); return; }
        RunPuppetRaw(go, ctx.RawText, ctx.Ct);
    }

    /// <summary>Parsed-args body. <paramref name="args"/> is never null.</summary>
    protected virtual void RunPuppet(GameObject puppet, GameArgumentParser.ParsedArgs args, CancellationToken ct)
        => RunPuppetRaw(puppet, "", ct);

    /// <summary>Raw-text body (parser-less verbs).</summary>
    protected virtual void RunPuppetRaw(GameObject puppet, string raw, CancellationToken ct)
        => puppet.Msg(PrintHelp());
}
