
namespace Atheriz.Core.Commands;

public static class CommandPermissions
{
    public static bool IsBuilder(IMessageTarget c) => c is GameObject g && g.IsBuilder;
    public static bool IsSuperUser(IMessageTarget c) => c is GameObject g && g.IsSuperUser;
}
