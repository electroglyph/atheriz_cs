// Port of atheriz/tests/test_parser_concurrency.py:1
using Atheriz.Core.Commands;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedParserConcurrencyTests
{
    private sealed class CounterCommand : Command
    {
        public override string Key => "counter";
        public override string Desc => "counter test";
        public int SetupCalls;
        protected override void SetupParser(GameArgumentParser p){ SetupCalls++; p.AddArgument("target", help:"target"); }
        public override void Run(IMessageTarget caller, object? args){ }
    }
    private sealed class EchoCommand : Command
    {
        public override string Key => "echo";
        public override string Desc => "echo test";
        protected override void SetupParser(GameArgumentParser p){ p.AddArgument("--verbose", action:"store_true"); p.AddArgument("msg", nargs:"?", defaultValue:""); }
        public override void Run(IMessageTarget caller, object? args){ }
    }

    [Fact]
    public void ParserInitThreadsafe()
    {
        using var env=GlobalTestEnv.Enter();
        var cmd=new CounterCommand();
        var f=typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        Assert.Null(f?.GetValue(cmd));
        var barrier=new Barrier(8);
        var parsers=new List<GameArgumentParser>(); var errors=new List<string>();
        var threads=Enumerable.Range(0,8).Select(_=> new Thread(()=>{ try{ barrier.SignalAndWait(5000); var p=cmd.Parser; lock(parsers) parsers.Add(p!); } catch(Exception ex){ lock(errors) errors.Add(ex.ToString()); }})).ToList();
        threads.ForEach(t=>t.Start()); threads.ForEach(t=>t.Join(5000));
        Assert.Empty(errors); Assert.Equal(8, parsers.Count); Assert.True(parsers.All(p=>ReferenceEquals(p,parsers[0]))); Assert.Equal(1, cmd.SetupCalls);
    }

    [Fact]
    public void ParseArgsConcurrent()
    {
        using var env=GlobalTestEnv.Enter();
        var cmd=new EchoCommand(); _=cmd.Parser;
        var barrier=new Barrier(8); var results=new List<(Delegate, object)>(); var errors=new List<string>();
        var threads=Enumerable.Range(0,8).Select(idx=> new Thread(()=>{ try{ var caller=new GameObject(); barrier.SignalAndWait(5000); var (run,_,args)=cmd.Execute(caller, idx%2==0? $"hello{idx} --verbose" : $"hello{idx}"); lock(results) results.Add((run!,args!)); } catch(Exception ex){ lock(errors) errors.Add(ex.ToString()); }})).ToList();
        threads.ForEach(t=>t.Start()); threads.ForEach(t=>t.Join(5000));
        Assert.Empty(errors); Assert.Equal(8, results.Count);
    }

    [Fact]
    public void ParserPickleExcludesLock()
    {
        using var env=GlobalTestEnv.Enter();
        var cmd=new EchoCommand(); _=cmd.Parser;
        var fld=typeof(Command).GetField("_parserLock", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        Assert.NotNull(fld);
        var clone=new EchoCommand(); Assert.NotNull(clone.Parser);
    }
}
