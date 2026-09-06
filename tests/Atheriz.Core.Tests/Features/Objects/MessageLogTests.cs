using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Per-object message log bound.
[Collection("Ported")]
public class MessageLogTests
{
    private static int MsgLogCount(GameObject o)
    {
        var f = typeof(GameObject).GetField("_msgLog", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        var list = (System.Collections.Generic.IReadOnlyCollection<string>)f!.GetValue(o)!;
        return list.Count;
    }

    // --- Channel history / msglog ---

    [Fact]
    public void GameObject_MsgLog_IsBounded()
    {
        // The per-object message log is bounded at 200 entries (exam dumps
        // ~60 lines, so whole multi-screen outputs must survive in
        // PeekMessages), matching Channel's capped history behavior.
        var o = GameObject.Create("npc");
        try
        {
            for (int i = 0; i < 300; i++) o.Msg($"m{i}");
            Assert.True(MsgLogCount(o) <= 200, $"msg log should be bounded, has {MsgLogCount(o)} entries");
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
