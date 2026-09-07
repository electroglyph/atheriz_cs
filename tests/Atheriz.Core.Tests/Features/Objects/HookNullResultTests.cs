using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// An after-hook replaces the result unconditionally, including with null
// for reference types (base_obj.py:64-66).
[Collection("Ported")]
public class HookNullResultTests
{
    [Atheriz.Core.Objects.After]
    public string? NullOut(string text, bool msgSelf, string prev) => null;

    [Fact]
    public void AfterHook_NullResult_Replaces()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("hooked");
            ObjectRegistry.AddObject(o);
            o.InstallHook("at_say", (Func<string, bool, string, string?>)NullOut);
            var res = o.Hookable<string>("at_say", () => "orig", "hi", true);
            Assert.Null(res);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
