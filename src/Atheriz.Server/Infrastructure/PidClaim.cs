namespace Atheriz.Server.Infrastructure;

/// <summary>
/// A claim held by <c>server.pid</c>: the pid plus, when the file carries
/// one, the UTC time the claim was recorded. The time ties the number to the
/// process that wrote it, so a pid the OS recycled later (its own start time
/// comes after the claim) is not mistaken for the old server. A legacy
/// single-line file has no time and is checked on process shape alone.
/// </summary>
internal readonly record struct PidClaim(int Pid, DateTime? WrittenUtc);
