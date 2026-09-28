namespace Atheriz.Core.Objects;

// Single source of truth for the hook map: each engine-hook constant carries
// its exact runtime string (the const value — renaming a value re-keys hooks)
// plus every dispatch arg-count shape. Atheriz.HookGen reads these at compile
// time and generates the HookName enum with its Name/TryParseName/
// DispatchArgCounts map, so the three spellings cannot drift.
[AttributeUsage(AttributeTargets.Field)]
internal sealed class HookArgsAttribute(params int[] counts) : Attribute
{
    public int[] Counts { get; } = counts;
}
