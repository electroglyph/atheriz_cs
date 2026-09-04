// Port of atheriz/tests/test_is_in_game_folder.py:1
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedIsInGameFolderTests
{
    private static string TempGameFolder()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"atheriz_game_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        return tmp;
    }

    [Fact]
    public void ReturnsTrueWithSettingsAndInitOnly()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "settings.py"), "# settings");
            File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try { Assert.True(GameUtils.IsInGameFolder()); } finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public void ReturnsFalseWhenSettingsMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try { Assert.False(GameUtils.IsInGameFolder()); } finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try { Directory.Delete(tmp, true);}catch{} }
    }

    [Fact]
    public void ReturnsFalseWhenInitMissing()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "settings.py"), "#");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try { Assert.False(GameUtils.IsInGameFolder()); } finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try { Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void ReturnsFalseWhenAtherizPyPresent()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "settings.py"), "#");
            File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
            File.WriteAllText(Path.Combine(tmp, "atheriz.py"), "# core");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try { Assert.False(GameUtils.IsInGameFolder()); } finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try { Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void ReturnsTrueWithoutSaveDir()
    {
        using var env = GlobalTestEnv.Enter();
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "settings.py"), "#");
            File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
            Assert.False(Directory.Exists(Path.Combine(tmp, "save")));
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try { Assert.True(GameUtils.IsInGameFolder()); } finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try { Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void NtCaseInsensitive()
    {
        using var env = GlobalTestEnv.Enter();
        // Windows branch case-insensitivity is wontfix style — verify Posix side still works
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "settings.py"), "#");
            File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try { Assert.True(GameUtils.IsInGameFolder()); } finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try { Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void IsInGameFolderWindowsBranch()
    {
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "settings.py"), "# settings");
            File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try
            {
                Assert.True(GameUtils.IsInGameFolder("nt"));
                File.WriteAllText(Path.Combine(tmp, "atheriz.py"), "# core");
                Assert.False(GameUtils.IsInGameFolder("nt"));
                File.Delete(Path.Combine(tmp, "atheriz.py"));
                Assert.True(GameUtils.IsInGameFolder("posix"));
                Assert.True(GameUtils.IsInGameFolder()); // default posix on linux
            }
            finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void IsInGameFolderNtCaseInsensitive()
    {
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "SETTINGS.PY"), "# settings");
            File.WriteAllText(Path.Combine(tmp, "__INIT__.PY"), "");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try
            {
                Assert.True(GameUtils.IsInGameFolder("nt"), "NT filesystem is case-insensitive, detection must be case-insensitive");
                File.Delete(Path.Combine(tmp, "SETTINGS.PY"));
                File.Delete(Path.Combine(tmp, "__INIT__.PY"));
                File.WriteAllText(Path.Combine(tmp, "settings.py"), "# settings");
                File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
                Assert.True(GameUtils.IsInGameFolder("posix"));
            }
            finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }

    [Fact]
    public void IsInGameFolderNtMixedCase()
    {
        var tmp = TempGameFolder();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "Settings.py"), "#");
            File.WriteAllText(Path.Combine(tmp, "__init__.py"), "");
            var old = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tmp);
            try
            {
                Assert.True(GameUtils.IsInGameFolder("nt"), "Windows case-insensitive check must handle mixed case");
                Assert.False(GameUtils.IsInGameFolder("posix")); // posix should be case-sensitive, Settings.py != settings.py
            }
            finally { Directory.SetCurrentDirectory(old); }
        }
        finally { try{Directory.Delete(tmp,true);}catch{} }
    }
}
