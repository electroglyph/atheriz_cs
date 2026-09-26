using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Correctness;

// Audit 10 finding 17: $eval on deep parentheses is capped at 32 nesting
// levels in both the literal evaluator and the arithmetic fallback (the
// fallback matters: _SafeEval tries the literal path first, so capping only
// the literal would shunt a deep input's stack overflow into SafeArithEval).
// Over the cap the expression echoes instead of burning O(depth x len) CPU.
// Neuter (drop either cap) returns the 50-char nested-list type name here.
[Collection("Ported")]
public sealed class EvalDepthCapTests
{
    private static (GameObject Pc, GameObject Receiver, Dictionary<string, object?> Mapping) Cast()
    {
        var pc = GameObject.Create("pindepthpc", isPc: true, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(pc);
        var receiver = GameObject.Create("pindepthrecv");
        ObjectRegistry.AddObject(receiver);
        return (pc, receiver, new Dictionary<string, object?>(StringComparer.Ordinal) { ["you"] = pc });
    }

    [Fact]
    public void EvalDeepParens_OverCap_EchoesInput()
    {
        using var _ = GlobalTestEnv.Enter();
        var (pc, receiver, mapping) = Cast();
        var inner = new string('(', 100) + new string(')', 100);
        var result = FuncParser.Parse("$eval(" + inner + ")", pc, receiver, mapping);
        Assert.Equal(inner, result);
    }

    [Fact]
    public void EvalShallowParens_StillEvaluates()
    {
        using var _ = GlobalTestEnv.Enter();
        var (pc, receiver, mapping) = Cast();
        var result = FuncParser.Parse("$eval(((1+2)))", pc, receiver, mapping);
        Assert.NotEqual("((1+2))", result);
    }
}
