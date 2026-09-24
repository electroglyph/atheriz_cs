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
            Assert.Equal(47, all.Count);
            var keys = all.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(
            [
                "ban", "build", "channel", "close", "create", "delete", "desc",
                "door", "drop", "emote", "examine", "follow", "get", "give",
                "group", "help", "inventory", "lock", "look", "map", "mapedit",
                "maze", "move", "nofollow", "none", "noun", "open", "puppet",
                "put", "quell", "quit", "reload", "save", "say", "screenreader",
                "set", "shutdown", "socials", "spam", "time", "unban", "unfollow",
                "unlock", "unpuppet", "unquell", "unset", "wander",
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
            Assert.Equal(8, all.Count);
            var keys = all.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(
                ["connect", "create", "guest", "help", "new", "none", "quit", "screenreader"],
                keys);
        }
        finally { CommandRegistry.Reset(); }
    }
}
