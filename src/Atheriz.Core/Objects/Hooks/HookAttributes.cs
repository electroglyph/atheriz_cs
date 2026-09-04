namespace Atheriz.Core.Objects;

[AttributeUsage(AttributeTargets.Method)]
public sealed class BeforeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class AfterAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class ReplaceAttribute : Attribute { }
