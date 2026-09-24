using Atheriz.Core.Network;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Commands.UnloggedIn;

// Shared puppet tail for the guest/new verbs (sync + async): atomic attach,
// default-home placement, post-puppet hook. One home so the four call sites
// cannot drift; the create verb has no puppet tail and stays out.
public static class CharacterPuppetSetup
{
    public static bool AttachAndHome(BaseConnection conn, GameObject character)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(character);
        if (!SessionPuppetHelper.TryAttach(conn, character)) return false;
        var nh = NodeHandler.GetCurrent();
        var home = nh?.GetNode(AtherizSettings.Global.DefaultHome);
        if (home is not null)
        {
            character.Home = Persistence.Dto.LocationRef.FromCoord(home.Coord);
            character.MoveTo(home);
        }
        try { character.AtPostPuppet(); } catch (Exception) { }
        return true;
    }
}
