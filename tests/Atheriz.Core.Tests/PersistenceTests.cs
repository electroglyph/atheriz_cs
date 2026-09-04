using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests;

public class PersistenceTests
{
    [Fact]
    public void DbContext_Guard_ThrowsWhenRelativeAndNotInGameFolder()
    {
        // CWD is /home/anon/atheriz-cs — not a game folder (no settings.py+__init__.py without atheriz.py)
        Assert.Throws<InvalidOperationException>(() => new AtherizDbContext("save"));
    }

    [Fact]
    public async Task DbContext_AbsolutePath_CreatesDatabase()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var ctx = new AtherizDbContext(tmp);
            await ctx.EnsureCreatedAsync();
            Assert.True(File.Exists(Path.Combine(tmp, "database.sqlite3")));
            // Second ensure is idempotent (like CREATE TABLE IF NOT EXISTS)
            await ctx.EnsureCreatedAsync();
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Objects_CanSaveAndLoad_Json()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            // create
            await using (var ctx = new AtherizDbContext(tmp))
            {
                await ctx.EnsureCreatedAsync();
                var dto = GameObjectDto.Create(1, "Hero");
                dto.IsPc = true;
                dto.Location = new LocationRef.CoordLocation(new Coord("limbo", 4, 4, 4));
                dto.Tags.Add("hero");
                dto.Locks.Add(new LockDefDto { Name = "view", Policy = "is_builder" });
                var row = new ObjectRow
                {
                    Id = dto.Id,
                    Type = dto.Type,
                    Version = dto.SchemaVersion,
                    Data = GameObjectDtoSerializer.ToJson(dto),
                };
                ctx.Objects.Add(row);
                await ctx.SaveChangesAsync();
            }
            // load
            await using (var ctx2 = new AtherizDbContext(tmp))
            {
                var row = await ctx2.Objects.FindAsync(1);
                Assert.NotNull(row);
                var dto = GameObjectDtoSerializer.FromJson(row!.Data);
                Assert.Equal("Hero", dto.Name);
                Assert.True(dto.IsPc);
                Assert.Contains("hero", dto.Tags);
                var loc = Assert.IsType<LocationRef.CoordLocation>(dto.Location);
                Assert.Equal("limbo", loc.Coord.Area);
            }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task InMemory_Context_Works()
    {
        var opts = new DbContextOptionsBuilder<AtherizDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var ctx = new AtherizDbContext(opts);
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Objects.Add(new ObjectRow { Id = 42, Data = "{}", Type = "object", Version = 1 });
        await ctx.SaveChangesAsync();
        var found = await ctx.Objects.FindAsync(42);
        Assert.NotNull(found);
    }
}
