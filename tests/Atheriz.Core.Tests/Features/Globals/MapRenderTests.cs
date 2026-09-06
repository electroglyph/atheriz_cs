using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Globals;

// Map grid rendering matches Python's full bounding box.
[Collection("Ported")]
public class MapRenderTests
{
    // --- RenderGrid unbounded ---

    [Fact]
    public void RenderGrid_RendersFullBoundingBox_LikePython()
    {
        // Python parity (map.py:164-183 render_grid renders the full bounding
        // box with no cap): capping would diverge from Python output for large
        // legitimate maps; sparse-grid blowup requires build rights, which is
        // a bigger problem already.
        var grid = new Dictionary<(int X, int Y), string> { [(0, 0)] = "#", [(2, 1)] = "#" };
        var (rendered, minX, maxY) = MapInfo.RenderGrid(grid);
        Assert.Equal(0, minX);
        Assert.Equal(1, maxY);
        Assert.Contains("#", rendered);
        var lines = rendered.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(3, lines[0].Length);
    }
}
