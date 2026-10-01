namespace Atheriz.Core.Objects;

/// <summary>
/// <c>(timestamp, sender, message)</c> tuples in
/// <c>atheriz/objects/base_channel.py</c>, plus a monotonic per-channel
/// sequence number (<c>Seq</c>, 0 when unknown, e.g. pre-upgrade saves)
/// that orders entries for live log replay.
/// </summary>
internal sealed record ChannelHistoryEntry(long Timestamp, string Sender, string Message, long Seq = 0);
