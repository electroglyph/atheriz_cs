namespace Atheriz.Core.Objects;

/// <summary>
/// <c>(timestamp, sender, message)</c> tuples in
/// <c>atheriz/objects/base_channel.py</c>.
/// </summary>
internal sealed record ChannelHistoryEntry(long Timestamp, string Sender, string Message);
