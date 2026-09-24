
namespace Atheriz.Core.Commands;

public static class CommandPermissions
{
    public static bool IsBuilder(IMessageTarget c) => c is GameObject g && g.IsBuilder;
    public static bool IsSuperUser(IMessageTarget c) => c is GameObject g && g.IsSuperUser;

    // Raw builder level without the quelled fold: quell/unquell themselves
    // must stay reachable while quelled (a quelled builder who could not
    // unquell would be stuck). One named home so audits grep a single
    // spelling instead of the inline `PrivilegeLevel >= Privilege.Builder`.
    public static bool HasBuilderPrivilege(IMessageTarget c) => c is GameObject g && g.PrivilegeLevel >= Privilege.Builder;
}
