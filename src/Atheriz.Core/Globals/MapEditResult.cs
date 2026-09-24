namespace Atheriz.Core.Globals;

public sealed class MapEditResult
{
    public const string Processed = MapEditStatus.Processed;
    public const string Retry = MapEditStatus.Retry;
    public const string Reject = MapEditStatus.Reject;

    public string Status { get; }
    public string Reason { get; }
    public string? NewKey { get; }
    public MapEditChain? Chain { get; }

    public MapEditResult(string status, string reason = "", string? newKey = null, MapEditChain? chain = null)
    {
        Status = status;
        Reason = reason;
        NewKey = newKey;
        Chain = chain;
    }
}
