using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The logged-in and unlogged-in registries keep their full verb sets
// when built from per-family builders.
[Collection("Ported")]
public sealed class CommandRegistryFamilyTests
{
    [Fact]
    public void RegistryLoggedIn_FamilyKeySetMatchesOriginalTotals()
    {
        CommandRegistry.Reset();
        try
        {
            var all = CommandRegistry.LoggedIn.GetAll();
            Assert.Equal(77, all.Count);
            var keys = all.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(
            [
                "about", "access", "accounts", "alias", "ban", "boot", "build",
                "channel", "chardelete", "close", "color", "copy", "create",
                "delete", "desc", "door", "drop", "emit", "emote", "examine",
                "find", "follow", "force", "get", "give", "group", "help",
                "home", "info", "inventory", "lock", "look", "map", "mapedit",
                "maze", "move", "name", "nick", "nofollow", "none", "noun",
                "objects", "open", "page", "password", "perm", "puppet", "put",
                "quell", "quit", "reload", "reset", "save", "say",
                "screenreader", "scripts", "sessions", "set", "shutdown",
                "socials", "spam", "tag", "teleport", "time", "unban",
                "unfollow", "unlock", "unpuppet", "unquell", "unset",
                "userpassword", "wall", "wander", "whisper", "who", "wipe",
                "yell",
            ], keys);
        }
        finally { CommandRegistry.Reset(); }
    }

    [Fact]
    public void RegistryUnloggedIn_FamilyKeySetMatchesOriginalTotals()
    {
        CommandRegistry.Reset();
        try
        {
            var all = CommandRegistry.UnloggedIn.GetAll();
            Assert.Equal(9, all.Count);
            var keys = all.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(
                ["connect", "create", "guest", "help", "info", "new", "none", "quit", "screenreader"],
                keys);
        }
        finally { CommandRegistry.Reset(); }
    }
}
