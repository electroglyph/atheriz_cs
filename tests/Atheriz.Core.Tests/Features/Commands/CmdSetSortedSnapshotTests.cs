using Atheriz.Core.Commands;

namespace Atheriz.Core.Tests.Features.Commands;

// The dispatch hot path reads an ordinal-sorted key snapshot that every
// mutation (Adds/Remove/RemoveByTag) invalidates: registering makes a prefix
// resolvable and removing makes it unresolvable through the real AutoAlias.
[Collection("Ported")]
public sealed class CmdSetSortedSnapshotTests
{
    private sealed class SnapCmd : Command
    {
        private readonly string _key;
        public SnapCmd(string key) => _key = key;
        public override string Key => _key;
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void SortedSnapshot_MatchesOrderedKeys()
    {
        var cs = new CmdSet();
        cs.Adds([new SnapCmd("look"), new SnapCmd("say"), new SnapCmd("emote")]);
        Assert.Equal(
            cs.GetKeys().OrderBy(k => k, StringComparer.Ordinal).ToList(),
            cs.GetSortedKeys().ToList());
    }

    [Fact]
    public void RegisterThenDispatch_PrefixResolves()
    {
        var cs = new CmdSet();
        var cmd = new SnapCmd("look");
        cs.Add(cmd);
        var (found, matched) = CommandDispatcher.AutoAlias(cs, "lo", socialsFallback: false);
        Assert.Same(cmd, found);
        Assert.Equal("look", matched);
    }

    [Fact]
    public void RemoveThenDispatch_PrefixNoLongerResolves()
    {
        var cs = new CmdSet();
        var cmd = new SnapCmd("look");
        cs.Add(cmd);
        cs.Remove(cmd);
        Assert.DoesNotContain("look", cs.GetSortedKeys());
        var (found, _) = CommandDispatcher.AutoAlias(cs, "lo", socialsFallback: false);
        Assert.Null(found);
    }

    [Fact]
    public void RemoveByTagThenDispatch_TaggedPrefixNoLongerResolves()
    {
        var cs = new CmdSet();
        var cmd = new SnapCmd("look");
        cs.Add(cmd, tag: "snaptag");
        cs.RemoveByTag("snaptag");
        Assert.DoesNotContain("look", cs.GetSortedKeys());
        var (found, _) = CommandDispatcher.AutoAlias(cs, "lo", socialsFallback: false);
        Assert.Null(found);
    }
}
