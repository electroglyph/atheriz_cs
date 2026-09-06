using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Behavior pins for filesystem permission helpers: best-effort chmod
// swallows failures and never throws, for missing and existing files.
[Collection("Ported")]
public class FilePermissionTests
{
    [Fact]
    public void FsUtil_TryChmod_MissingFile_NoThrow()
    {
        // "Try*" contract: failure is swallowed, never an exception.
        FsUtil.TryChmod0600(Path.Combine(Path.GetTempPath(), "no_such_atheriz_xyz"));
        FsUtil.TryChmod0700(Path.Combine(Path.GetTempPath(), "no_such_atheriz_xyz"));
    }

    [Fact]
    public void FsUtil_TryChmod_ExistingFile_NoThrow()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            FsUtil.TryChmod0600(tmp);
            FsUtil.TryChmod0700(tmp);
            Assert.True(File.Exists(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void FsUtil_ChmodAliases_Agree()
    {
        // TryChmod0600/TrySet0600 are two names for one operation.
        var tmp = Path.GetTempFileName();
        try
        {
            FsUtil.TryChmod0600(tmp);
            FsUtil.TrySet0600(tmp);
            Assert.True(File.Exists(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
