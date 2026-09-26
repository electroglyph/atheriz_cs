using System.Reflection;
using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core.Tests.Features.Network;

// Finding 6: DrainInput's catch logged through a throwing ILoggerFactory,
// faulting the pool task with _inputRunning stuck true and wedging the
// connection's input pipeline for life. The catch now uses the robust path
// and the loop releases _inputRunning in a finally, so a second message
// still drains.
[Collection("Ported")]
public sealed class DrainInputRobustLogTests
{
    private sealed class ThrowFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => throw new InvalidOperationException("logger factory is broken");
        public void Dispose() { }
    }

    [Fact]
    public void DrainInput_LoggerFactoryThrows_SecondMessageStillDrains()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        // AtherizLogger is process-global and GlobalTestEnv does not reset
        // it: save and restore the factory so no other test observes this.
        var factoryField = typeof(Atheriz.Core.AtherizLogger).GetField("_factory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(factoryField);
        var prev = factoryField!.GetValue(null);
        Atheriz.Core.AtherizLogger.Configure(new ThrowFactory());
        var conn = new TestConnection();
        try
        {
            int runs = 0;
            Action<BaseConnection, List<object?>, Dictionary<string, object?>> handler = (_, _, _) =>
            {
                Interlocked.Increment(ref runs);
                throw new InvalidOperationException("handler failure");
            };
            conn.EnqueueInput(handler, [], []);
            Assert.True(PortedHelpers.WaitFor(() => Volatile.Read(ref runs) >= 1, 5000), "first handler ran");
            conn.EnqueueInput(handler, [], []);
            Assert.True(PortedHelpers.WaitFor(() => Volatile.Read(ref runs) >= 2, 5000),
                "second handler ran — the input pipeline stayed drainable despite the throwing logger factory");
        }
        finally
        {
            factoryField!.SetValue(null, prev);
            conn.SetDisconnected(true);
            conn.ClearPendingInput();
            ConnectionManager.GlobalInstance = null;
            mgr.Atp.Stop(wait: false);
        }
    }
}
