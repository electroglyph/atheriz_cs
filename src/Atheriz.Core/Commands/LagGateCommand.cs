namespace Atheriz.Core.Commands;

/// <summary>
/// Lag-gate decorator: wraps any <see cref="Command"/> and skips invocation
/// when <paramref name="gate"/> reports the caller lagged. The static
/// <see cref="Command.GlobalLagCheck"/> / <see cref="CommandDispatcher.LagCheck"/>
/// hooks stay as the install surface (game code sets them); they now read as
/// "wrap the command in this" rather than hidden statics consulted deep
/// inside <c>Execute</c>. All identity members forward to the inner command
/// so help, aliases, access and parser behavior are unchanged.
/// </summary>
public sealed class LagGateCommand(Command inner, Func<IMessageTarget, bool> gate) : Command
{
    public override string Key => inner.Key;
    public override IReadOnlyList<string> Aliases => inner.Aliases;
    public override string Desc => inner.Desc;
    public override string ExtraDesc => inner.ExtraDesc;
    public override string Category => inner.Category;
    public override string Tag { get => inner.Tag; set => inner.Tag = value; }
    public override bool Hide => inner.Hide;
    public override bool UseParser => inner.UseParser;
    public override bool Access(IMessageTarget caller) => inner.Access(caller);
    public override GameArgumentParser? Parser { get => inner.Parser; set => inner.Parser = value; }

    protected override void SetupParser(GameArgumentParser parser) => inner.SetupParserForwarder(parser);

    public override void Run(CommandContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (gate(ctx.Caller)) return;
        inner.Run(ctx);
    }

    public override (Action<IMessageTarget, object?>? func, IMessageTarget? caller, object? args) Execute(
        IMessageTarget caller, string argsString, string cmdstring = "")
    {
        var (func, c, a) = inner.Execute(caller, argsString, cmdstring);
        if (func is null) return (func, c, a);
        return ((tgt, argv) => { if (!gate(tgt)) func(tgt, argv); }, c, a);
    }
}
