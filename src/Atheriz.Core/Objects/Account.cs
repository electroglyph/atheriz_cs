using System.Security.Cryptography;
using System.Text;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of <c>atheriz/objects/base_account.py:Account</c>.
/// Inherits GameObject flags (is_account) but adds Account-specific state.
/// </summary>
public class Account : GameObject
{
    public new static bool _is_thread_safe = true;
    public static bool GroupSave => false; // Fix for test_account.py:39
    // Hooks for testing — mirrors Python monkeypatch of at_create/at_delete
    public static Action<Account>? AtCreateHook { get; set; }
    public static Func<GameObject?, bool>? AtDeleteHook { get; set; }

    private string _passwordHash = "";
    private List<int> _characters = [];
    private string _banReason = "";
    private bool _loggedIn;

    public Account()
    {
        IsAccount = true;
    }
    public override void AtCreate()
    {
        base.AtCreate();
        AtCreateHook?.Invoke(this);
    }
    public override bool AtDelete(GameObject caller)
    {
        if (AtDeleteHook != null) return AtDeleteHook(caller);
        return true; // Fix for test_account.py:88 Account.at_delete is unconditional true, not access-gated
    }
    public virtual bool AtPrePuppet(GameObject character) => Hookable("at_pre_puppet", () => true, character); // Fix for test_account.py:408 port of base_account.py:76 at_pre_puppet
    // Account-specific Delete returns bool (Python) — hides GameObject tuple version.
    // NOTE: C# cannot override with a different return type, so a GameObject-typed
    // reference still dispatches to the base tuple Delete. That path converges via
    // del ops at the next save; this bool path deletes the row immediately.
    public new bool Delete(GameObject? caller = null, bool unused = true)
    {
        // Port of base_account.py:53 delete.
        if (!AtDelete(caller!)) return false;
        var ops = new List<(string Sql, object[] Params)>();
        if (!IsTemporary) ops.Add(GetDelOps());
        // Mark deleted and unregister BEFORE the DB delete so a concurrent
        // checkpoint cannot resurrect the row. Mirrors Node.delete.
        SyncRoot.EnterWriteLock();
        try { IsDeleted = true; } finally { SyncRoot.ExitWriteLock(); }
        ObjectRegistry.RemoveObject(this);
        if (ops.Count > 0)
        {
            try
            {
                // Shared save-path resolution (same as ObjectRegistry.SaveObjects()):
                // ATHERIZ_SAVE_PATH override, else configured SavePath.
                var savePath = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? Settings.AtherizSettings.Global.SavePath;
                using var db = new Persistence.AtherizDbContext(savePath);
                db.Database.EnsureCreated();
                ObjectRegistry.DeleteObjects(db, ops);
            }
            catch
            {
                // DB failure: roll back so the account stays live (base_account.py:78-82).
                SyncRoot.EnterWriteLock();
                try { IsDeleted = false; } finally { SyncRoot.ExitWriteLock(); }
                ObjectRegistry.AddObject(this);
                throw;
            }
        }
        return true;
    }

    public string PasswordHash
    {
        get => ReadHash();
        private set => WriteHash(value);
    }
    public IReadOnlyList<int> Characters => ReadChars();
    public override string BanReason { get => ReadBan(); set => WriteBan(value); }
    public bool LoggedIn { get => ReadLogged(); private set => WriteLogged(value); }

    private string ReadHash() { SyncRoot.EnterReadLock(); try { return _passwordHash; } finally { SyncRoot.ExitReadLock(); } }
    private void WriteHash(string v) { SyncRoot.EnterWriteLock(); try { _passwordHash = v; IsModified = true; } finally { SyncRoot.ExitWriteLock(); } }
    private IReadOnlyList<int> ReadChars() { SyncRoot.EnterReadLock(); try { return new List<int>(_characters); } finally { SyncRoot.ExitReadLock(); } }
    private string ReadBan() { SyncRoot.EnterReadLock(); try { return _banReason; } finally { SyncRoot.ExitReadLock(); } }
    private void WriteBan(string v) { SyncRoot.EnterWriteLock(); try { _banReason = v; IsModified = true; } finally { SyncRoot.ExitWriteLock(); } }
    private bool ReadLogged() { SyncRoot.EnterReadLock(); try { return _loggedIn; } finally { SyncRoot.ExitReadLock(); } }
    private void WriteLogged(bool v) { SyncRoot.EnterWriteLock(); try { _loggedIn = v; } finally { SyncRoot.ExitWriteLock(); } }

    public static string HashPassword(string password, string? saltOverride = null)
    {
        var salt = saltOverride ?? SaltProvider.GetSalt();
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        // 600k iterations SHA256, matching Python hashlib.pbkdf2_hmac 600_000
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 600_000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32); // 256-bit
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool CheckPassword(string password, string? saltOverride = null)
    {
        var hash = HashPassword(password, saltOverride);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(PasswordHash));
    }

    public void SetPassword(string password, string? saltOverride = null)
    {
        PasswordHash = HashPassword(password, saltOverride);
    }

    public bool Login(string name, string password, string? saltOverride = null)
    {
        // Snapshot under a read lock, verify outside: PBKDF2 is ~100ms of CPU and
        // must not block all readers under the write lock (audit F007). Python
        // holds its RLock throughout, but C# readers would starve; last-writer-wins
        // on _loggedIn preserves the observable outcome.
        string curName;
        string curHash;
        SyncRoot.EnterReadLock();
        try { curName = Name; curHash = _passwordHash; }
        finally { SyncRoot.ExitReadLock(); }
        var hash = HashPassword(password, saltOverride);
        bool ok = string.Equals(curName, name, StringComparison.OrdinalIgnoreCase)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(curHash));
        SyncRoot.EnterWriteLock();
        try { _loggedIn = ok; }
        finally { SyncRoot.ExitWriteLock(); }
        return ok;
    }

    public void AddCharacter(GameObject character)
    {
        SyncRoot.EnterWriteLock();
        try { if (!_characters.Contains(character.Id)) { _characters.Add(character.Id); IsModified = true; } }
        finally { SyncRoot.ExitWriteLock(); }
    }
    public void RemoveCharacter(GameObject character)
    {
        SyncRoot.EnterWriteLock();
        try { if (_characters.Remove(character.Id)) IsModified = true; }
        finally { SyncRoot.ExitWriteLock(); }
    }

    public override void AtDisconnect()
    {
        SyncRoot.EnterWriteLock();
        try { _loggedIn = false; }
        finally { SyncRoot.ExitWriteLock(); }
        base.AtDisconnect();
    }


    public static Account Create(string name, string password, string? saltOverride = null, Func<string,bool>? existsCheck = null)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Name and password must not be empty.");
        if (existsCheck is not null && existsCheck(name))
            throw new InvalidOperationException($"Account with this name ({name}) already exists.");
        var acc = new Account();
        acc.Id = GameObject.GetNextId();
        acc.Name = name;
        acc.SyncRoot.EnterWriteLock();
        try
        {
            acc._passwordHash = HashPassword(password, saltOverride);
            acc._characters = [];
            acc._banReason = "";
            acc._loggedIn = false;
            acc.IsModified = true;
            acc.IsAccount = true;
        }
        finally { acc.SyncRoot.ExitWriteLock(); }
        acc.AtCreate();
        // Atomic register — mirrors add_object_unique for race safety (port of test_duplicate_create_race.py)
        ObjectRegistry.AddObjectUnique(acc, o => o is Account a && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase), $"Account with this name ({name}) already exists.");
        return acc;
    }

    public override (string Sql, object[] Params) GetSaveOps()
    {
        bool had;
        string json;
        SyncRoot.EnterWriteLock();
        try
        {
            had = IsModified;
            IsModified = false;
            try { json = GameObjectDtoSerializer.ToJson(ToDto()); }
            finally { IsModified = had; }
        }
        finally { SyncRoot.ExitWriteLock(); }
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [Id, json]);
    }
    public override (string Sql, object[] Params) GetSaveOpsClearing()
    {
        string json;
        SyncRoot.EnterWriteLock();
        try
        {
            var dto = ToDto();
            dto.IsModified = false;
            json = GameObjectDtoSerializer.ToJson(dto);
            IsModified = false;
        }
        catch
        {
            throw;
        }
        finally { SyncRoot.ExitWriteLock(); }
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [Id, json]);
    }

    // DTO extension: store account fields in Extra for persistence simplicity
    // Fix for test_persistence.py:227 logged_in not persisted — mirrors __getstate__ setting logged_in=False
    public override GameObjectDto ToDto()
    {
        var dto = base.ToDto();
        dto.Type = "account";
        // Stash account extras via Extra dictionary (JSON) — never persist logged_in true
        dto.Extra["password"] = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(_passwordHash)).RootElement.Clone();
        dto.Extra["characters"] = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(_characters)).RootElement.Clone();
        dto.Extra["banReason"] = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(_banReason)).RootElement.Clone();
        dto.Extra["loggedIn"] = System.Text.Json.JsonDocument.Parse("false").RootElement.Clone();
        return dto;
    }

    public new static Account FromDto(GameObjectDto dto)
    {
        var acc = new Account();
        acc.Id = dto.Id;
        // Use shared GameObject field copy (internal) to avoid recursion and duplication
        GameObject.ApplyDtoFields(acc, dto, null);
        // Ensure IsAccount flag true without leaving dirty flag if dto was clean
        bool wantModified = dto.IsModified;
        acc.IsAccount = true;
        acc.IsModified = wantModified;
        // restore account extras if present (private fields direct, no dirty mark)
        if (dto.Extra.TryGetValue("password", out var pw))
        {
            if (pw.ValueKind == System.Text.Json.JsonValueKind.String) acc._passwordHash = pw.GetString() ?? "";
            else acc._passwordHash = pw.GetRawText().Trim('"');
        }
        if (dto.Extra.TryGetValue("characters", out var ch) && ch.ValueKind == System.Text.Json.JsonValueKind.Array)
            acc._characters = ch.EnumerateArray().Select(e => e.GetInt32()).ToList();
        else if (!dto.Extra.ContainsKey("characters")) acc._characters = [];
        if (dto.Extra.TryGetValue("banReason", out var br))
        {
            if (br.ValueKind == System.Text.Json.JsonValueKind.String) acc._banReason = br.GetString() ?? "";
            else acc._banReason = br.GetRawText().Trim('"');
        }
        if (dto.Extra.TryGetValue("loggedIn", out var li) && li.ValueKind == System.Text.Json.JsonValueKind.True) acc._loggedIn = true;
        else acc._loggedIn = false;
        acc.IsModified = wantModified;
        return acc;
    }
}
