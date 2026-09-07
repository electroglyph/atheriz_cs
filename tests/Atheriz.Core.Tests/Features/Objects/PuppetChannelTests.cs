using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// AtPostPuppet must use a typed channel check: the dynamic fallback dispatches
// AddListener into any IsChannel object and swallows the binder failure, so
// membership is silently skipped (GameObject.Puppet.cs:179-182).
[Collection("Ported")]
public class PuppetChannelTests
{
    [Fact]
    public void AtPostPuppet_IgnoresNonChannelIsChannelObject()
    {
        // Correct: an IsChannel object that is not a Channel never receives a
        // listener call; only typed channels are re-subscribed.
        ObjectRegistry.ClearAll();
        try
        {
            var fake = new NonChannelIsChannelObject();
            fake.Id = 5101;
            ObjectRegistry.AddObject(fake);
            var puppet = GameObject.Create("puppet");
            var field = typeof(GameObject).GetField("_channels", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((List<int>)field.GetValue(puppet)!).Add(fake.Id);

            puppet.AtPostPuppet();
            Assert.False(fake.ListenerAdded);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// IsChannel but not a Channel: has an AddListener-shaped method the dynamic
// fallback would bind to, which the typed check must not touch.
public sealed class NonChannelIsChannelObject : GameObject
{
    public bool ListenerAdded;

    public NonChannelIsChannelObject()
    {
        IsChannel = true;
    }

    public void AddListener(GameObject o)
    {
        ListenerAdded = true;
    }
}
