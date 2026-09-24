using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands;

/// <summary>
/// Typed invocation for a command: who invoked, the resolved puppet (logged-in
/// path), parsed args and/or the raw text, plus cancellation. Replaces the
/// old <c>object? args</c> dual-type (`ParsedArgs` vs <c>string</c>) bag the
/// base splits for the caller.
/// </summary>
public sealed record CommandContext(
    IMessageTarget Caller,
    GameObject? Puppet,
    GameArgumentParser.ParsedArgs? Args,
    string RawText,
    CancellationToken Ct);
