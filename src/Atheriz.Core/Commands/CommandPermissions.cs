// Port of atheriz/objects/base_obj.py:IsBuilder/IsSuperUser privilege helpers (faithful to Python privilege checks)
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands;

public static class CommandPermissions
{
    public static bool IsBuilder(IMessageTarget c) => c is GameObject g && g.IsBuilder;
    public static bool IsSuperUser(IMessageTarget c) => c is GameObject g && g.IsSuperUser;
}
