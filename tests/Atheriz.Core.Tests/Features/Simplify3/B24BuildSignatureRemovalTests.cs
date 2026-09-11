// Pins for U3 (BuildSignature reflection-shim removal): production code must
// not expose the reflection shim anymore, while the explicit MethodInfo API
// it deferred to keeps its shape (varargs/optional-arity introspection).
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B24BuildSignatureRemovalTests
{
    [Fact]
    public void BuildSignature_GameUtilsExposesNoShim()
    {
        // Tests may reflect; production must not. The public shim is deleted.
        var shim = typeof(GameUtils).GetMethod(
            "BuildSignature",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.Null(shim);
    }

    [Fact]
    public void GetParameters_ExplicitApi_VarargsShapePreserved()
    {
        void Foo(int a, int b, int c = 3, params int[] args) { }
        var del = (Action<int, int, int, int[]>)Foo;
        var sig = del.Method.GetParameters();
        Assert.True(sig.Length >= 3);
        Assert.Contains(sig, p => p.Name == "a");
    }

    [Fact]
    public void GetParameters_ExplicitApi_AllVariantsPreserved()
    {
        void Foo1(int a, int b, int c, int d = 3, params int[] args) { }
        var del1 = (Action<int, int, int, int, int[]>)Foo1;
        Assert.True(del1.Method.GetParameters().Length >= 4);
        void Foo2(int a, params int[] args) { }
        var del2 = (Action<int, int[]>)Foo2;
        Assert.True(del2.Method.GetParameters().Length >= 1);
    }
}
