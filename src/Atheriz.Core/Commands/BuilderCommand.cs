namespace Atheriz.Core.Commands;

/// <summary>
/// Base for builder-only commands. Seals <see cref="Command.Access"/> to the
/// single <see cref="CommandPermissions.IsBuilder"/> gate so audits grep one
/// spelling. (Quell/unquell intentionally stay on
/// <see cref="CommandPermissions.HasBuilderPrivilege"/> — the raw level
/// without the quelled fold — because a quelled builder must still reach
/// them.)
/// </summary>
public abstract class BuilderCommand : LoggedInCommand
{
    public sealed override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
}
