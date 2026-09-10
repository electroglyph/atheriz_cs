using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// JsonElement-object normalization shared by legend validate/apply and
// IsLegendEntry: dicts pass through, JSON objects convert, everything else
// is null (rejected by callers exactly as before).
[Collection("Ported")]
public sealed class MapEditLegendNormalizationTests
{
    private static readonly MethodInfo NormalizeDictMethod =
        typeof(InputFuncs).GetMethod("NormalizeDict", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static JsonElement Je(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static Dictionary<string, object?>? Norm(object? v) =>
        (Dictionary<string, object?>?)NormalizeDictMethod.Invoke(null, [v]);

    [Fact]
    public void NormalizeDict_PassthroughConvertOrNull()
    {
        var dict = new Dictionary<string, object?> { ["symbol"] = "@" };
        Assert.Same(dict, Norm(dict));
        var converted = Norm(Je("{\"symbol\":\"@\",\"n\":3}"));
        Assert.NotNull(converted);
        Assert.Equal("@", converted!["symbol"]);
        // JsonElementToObject's int/long/double ternary unifies to double
        // (pre-existing shape) — numbers normalize to double, not int.
        Assert.Equal(3.0, converted!["n"]);
        Assert.Null(Norm("nope"));
        Assert.Null(Norm(5));
        Assert.Null(Norm(null));
        Assert.Null(Norm(Je("[1,2]")));
        Assert.Null(Norm(Je("\"s\"")));
    }

    [Fact]
    public void LegendHandler_JsonElementEntries_ValidatedLikeDicts()
    {
        using var env = GlobalTestEnv.Enter();
        var funcs = new InputFuncs();
        // Valid JsonElement object normalizes and passes validation, so
        // consume (unknown key) — not the entry check — rejects it.
        var ok = new TestConnection();
        funcs.MapEditLegendHandler(ok, ["nope", 1, new List<object?> { Je("{\"symbol\":\"@\"}") }], []);
        Assert.Single(ok.Sent);
        Assert.Equal("map_edit_reject", ok.Sent[0].Cmd);
        Assert.Equal("unknown_key", ok.Sent[0].Args[0]);
        // Invalid JsonElement object is rejected at its index.
        var bad = new TestConnection();
        funcs.MapEditLegendHandler(bad, ["k", 1, new List<object?> { Je("{\"symbol\":\"\"}") }], []);
        Assert.Single(bad.Sent);
        Assert.Equal("Invalid legend entry at index 0.", bad.Sent[0].Args[0]);
        // Non-object entries are rejected the same way.
        var bad2 = new TestConnection();
        funcs.MapEditLegendHandler(bad2, ["k", 1, new List<object?> { "nope" }], []);
        Assert.Single(bad2.Sent);
        Assert.Equal("Invalid legend entry at index 0.", bad2.Sent[0].Args[0]);
    }
}
