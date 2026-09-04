// Port of atheriz/tests/test_base_cmdset.py:1
using Atheriz.Core.Commands;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedBaseCmdSetTests
{
    private sealed class FakeCommand : Command
    {
        private readonly string _key;
        private readonly IReadOnlyList<string> _aliases;
        public FakeCommand(string key = "fake", IReadOnlyList<string>? aliases = null, string tag = "")
        {
            _key = key;
            _aliases = aliases ?? [];
            Tag = tag;
        }
        public override string Key => _key;
        public override IReadOnlyList<string> Aliases => _aliases;
        public override string Desc => $"Fake {_key}";
        public override string Category => "Test";
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact] public void CmdSet_Init_Empty() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); Assert.Empty(cs.GetKeys()); var f = typeof(CmdSet).GetField("_lock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); var l = f!.GetValue(cs); Assert.IsType<ReaderWriterLockSlim>(l); }
    [Fact] public void GetAll_EmptyReturnsEmptyList() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); Assert.Empty(cs.GetAll()); }
    [Fact] public void GetAll_ReturnsList() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); cs.Add(new FakeCommand("a")); cs.Add(new FakeCommand("b")); var r = cs.GetAll(); Assert.IsType<List<Command>>(r); Assert.Equal(2, r.Count); }
    [Fact] public void GetAll_IncludesAliasesAsSameObject() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("a", ["x","y"]); cs.Add(c); var r = cs.GetAll(); Assert.Equal(3, r.Count); Assert.All(r, x => Assert.Same(c, x)); }
    [Fact] public void GetAll_UniqueInstances() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a"); var b = new FakeCommand("b"); cs.Add(a); cs.Add(b); var r = cs.GetAll(); Assert.Equal(2, r.Distinct().Count()); }
    [Fact] public void Add_ByKey() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("a"); cs.Add(c); Assert.Same(c, cs.Get("a")); }
    [Fact] public void Add_WithAliases() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("a", ["x","y"]); cs.Add(c); Assert.Same(c, cs.Get("a")); Assert.Same(c, cs.Get("x")); Assert.Same(c, cs.Get("y")); }
    [Fact] public void Add_OverwritesExistingKey_Throws() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a"); var b = new FakeCommand("a"); cs.Add(a); var ex = Assert.Throws<InvalidOperationException>(() => cs.Add(b)); Assert.Contains("'a' already registered", ex.Message); }
    [Fact] public void Add_OverwritesExistingAlias_Throws() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a", ["x"]); var b = new FakeCommand("b", ["x"]); cs.Add(a); var ex = Assert.Throws<InvalidOperationException>(() => cs.Add(b)); Assert.Contains("'x' already registered", ex.Message); }
    [Fact] public void Add_WithTagSetsCommandTag() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("a"); cs.Add(c, tag: "mytag"); Assert.Equal("mytag", c.Tag); }
    [Fact] public void Add_WithoutTagDoesNotOverwrite() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("a", tag: "existing"); cs.Add(c); Assert.Equal("existing", c.Tag); }
    [Fact] public void Add_NoAliases() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("a", []); cs.Add(c); Assert.Single(cs.GetKeys()); Assert.Same(c, cs.Get("a")); }
    [Fact] public void Add_SameInstanceReaddIsNoop() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("a", ["x","y"]); cs.Add(c); cs.Add(c); Assert.Same(c, cs.Get("x")); }
    [Fact] public void Add_KeyEqualOwnAliasAllowed() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var c = new FakeCommand("x", ["x"]); cs.Add(c); Assert.Same(c, cs.Get("x")); }
    [Fact] public void Add_AliasShadowingCommandKeyThrows() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a", ["x"]); var b = new FakeCommand("x"); cs.Add(a); var ex = Assert.Throws<InvalidOperationException>(() => cs.Add(b)); Assert.Contains("'x' already registered", ex.Message); }
    [Fact] public void Add_AliasVsAliasThrows() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a", ["x"]); var b = new FakeCommand("b", ["x"]); cs.Add(a); Assert.Throws<InvalidOperationException>(() => cs.Add(b)); }
    [Fact] public void Adds_Multiple() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a"); var b = new FakeCommand("b"); var c = new FakeCommand("c"); cs.Adds([a,b,c]); Assert.Same(a, cs.Get("a")); Assert.Same(b, cs.Get("b")); }
    [Fact] public void Adds_WithAliases() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a", ["x"]); var b = new FakeCommand("b", ["y"]); cs.Adds([a,b]); Assert.Same(a, cs.Get("x")); Assert.Same(b, cs.Get("y")); }
    [Fact] public void Adds_WithTagSetsAll() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a"); var b = new FakeCommand("b"); cs.Adds([a,b], tag: "batch"); Assert.Equal("batch", a.Tag); Assert.Equal("batch", b.Tag); }
    [Fact] public void Adds_WithoutTagPreservesExisting() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); var a = new FakeCommand("a", tag: "preserved"); cs.Adds([a]); Assert.Equal("preserved", a.Tag); }
    [Fact] public void Adds_EmptyList() { using var env = GlobalTestEnv.Enter(); var cs = new CmdSet(); cs.Adds([]); Assert.Empty(cs.GetKeys()); }
}
