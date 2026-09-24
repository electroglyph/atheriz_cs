namespace Atheriz.Core.Commands.LoggedIn;

// extension helpers for Channel/GameObject group handling
internal static class GroupExtensions
{
    public static void RemoveGroupChannel(this GameObject go)
    {
        go.GroupChannel = null;
    }
}
