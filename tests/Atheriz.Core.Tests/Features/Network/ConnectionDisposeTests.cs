using Atheriz.Core.Network;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

// Connection teardown must release native resources, not just flip flags.
[Collection("Ported")]
public class ConnectionDisposeTests
{
    // Minimal writer double: an input to the real TelnetConnection, recording
    // whether Close vs Dispose was invoked (fakes for sockets are inputs only).
    private sealed class DisposeTrackingWriter : ITelnetWriter, IDisposable
    {
        public bool Disposed { get; private set; }
        public bool Closed { get; private set; }
        public void Write(string text) { }
        public void Iac(byte cmd, byte opt) { }
        public void Close() { Closed = true; }
        public int? GetWriteBufferSize() => null;
        public void SetExtCallback(byte opt, Action<int, int> callback) { }
        public string? GetPeerHost() => "127.0.0.1";
        public void Dispose() { Disposed = true; }
    }

    [Fact]
    public void Disconnect_ReleasesConnectionResources()
    {
        // Behavior: Disconnect must release the connection's resources.
        // TelnetConnection.Dispose at TelnetProtocol.cs:61-69 disposes the
        // writer/reader and WebSocketConnection.Dispose at
        // WebSocketProtocol.cs:43-52 disposes the socket plus _sendLock, but
        // Disconnect at ConnectionManager.cs:859 calls only Close() — Dispose
        // has zero callers in src/, so every connection leaks its semaphore
        // (a kernel handle once contended) until finalization.
        var mgr = PortedHelpers.MakeManager();
        var prevGlobal = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var writer = new DisposeTrackingWriter();
            var conn = new TelnetConnection(new StringReader(""), writer, "conn_dispose");
            Assert.True(mgr.RegisterConnection("conn_dispose", conn));
            mgr.Disconnect(conn);
            Assert.True(writer.Disposed, "Disconnect must dispose the connection (writer released)");
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }
}
