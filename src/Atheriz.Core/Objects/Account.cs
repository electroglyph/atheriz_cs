using System.Security.Cryptography;
using System.Text;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of <c>atheriz/objects/base_account.py:Account</c>.
/// Inherits GameObject flags (is_account) but adds Account-specific state.
/// </summary>
public class Account : GameObject
{
    internal new static bool _is_thread_safe = true;
    public static bool GroupSave => false; // Fix for test_account.py:39

    private string _passwordHash = "";
    private List<int> _characters = [];
    private string _banReason = "";
    private bool _loggedIn;

    public Account()
    {
        IsAccount = true;
    }
    public override bool AtDelete(GameObject? caller)
    {
        // Unconditional true (test_account.py:88 — not access-gated like the base),
        // routed through the hook pipeline so game code can veto via at_delete hooks.
        return Hookable("at_delete", () => true, caller);
    }
    public virtual bool AtPrePuppet(GameObject character) => Hookable("at_pre_puppet", () => true, character); // Fix for test_account.py:408 port of base_account.py:76 at_pre_puppet
    // Account-specific Delete returns bool (Python) — hides GameObject tuple version.
    // NOTE: C# cannot override with a different return type, so a GameObject-typed
    // reference dispatches to the base tuple Delete. That path converges via
    // DeleteImmediate below (same immediate row delete), keeping both static types unified.
    public new bool Delete(GameObject? caller = null, bool unused = true)
    {
        return DeleteImmediate(caller) is not null;
    }

    // Shared immediate-delete core for both static types .
    internal (int count, List<object> ops)? DeleteImmediate(GameObject? caller)
    {
        // Port of base_account.py:53 delete.
        if (!AtDelete(caller)) return null;
        List<(string Sql, object[] Params)> ops = [];
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
                // Shared save-path resolution via the factory (same as ObjectRegistry.SaveObjects()):
                // ATHERIZ_SAVE_PATH override, else configured SavePath.
                var savePath = AtherizDbContextFactory.ResolveSavePath(Settings.AtherizSettings.Global);
                using var db = new Persistence.AtherizDbContext(savePath);
                db.Database.EnsureCreated();
                ObjectRegistry.DeleteObjects(db, ops.Select(o => Convert.ToInt32(o.Params[0])).ToList());
            }
            catch
            {
                // DB failure: roll back so the account stays live (base_account.py:78-82).
                SyncRoot.EnterWriteLock();
                try { IsDeleted = false; } finally { SyncRoot.ExitWriteLock(); }
                // The rollback re-add must not mask the original DB failure:
                // a duplicate-id throw here would replace the real error.
                try { ObjectRegistry.AddObject(this); }
                catch (Exception rbEx) { AtherizLogger.LogDebug("Suppressed Account.DeleteImmediate rollback: " + rbEx.Message, "Account"); }
                throw;
            }
        }
        var boxed = new List<object>(ops.Count);
        foreach (var op in ops) boxed.Add(op);
        return (1, boxed);
    }

    public string PasswordHash
    {
        get => Read(() => _passwordHash);
        private set => Write(() => { _passwordHash = value; IsModified = true; });
    }
    public IReadOnlyList<int> Characters => Read(() => (IReadOnlyList<int>)new List<int>(_characters));
    public override string BanReason { get => Read(() => _banReason); set => Write(() => { _banReason = value; IsModified = true; }); }
    // LoggedIn is transient session state, not persisted save data — intentionally not marked modified.
    public bool LoggedIn { get => Read(() => _loggedIn); private set => Write(() => _loggedIn = value); }

    public override IEnumerable<(string name, object? value, bool isProperty)> GetExamMembers()
    {
        foreach (var m in base.GetExamMembers()) yield return m;
        object? Safe(Func<object?> f) { try { return f(); } catch { return "<error>"; } }
        yield return ("Characters", Safe(() => (object?)Characters), true);
        yield return ("BanReason", Safe(() => (object?)BanReason), true);
        yield return ("LoggedIn", Safe(() => (object?)LoggedIn), true);
    }

    public static string HashPassword(string password, string? saltOverride = null)
    {
        var salt = saltOverride ?? SaltProvider.GetSalt();
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        // 600k iterations SHA256, matching Python hashlib.pbkdf2_hmac 600_000
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 600_000, HashAlgorithmName.SHA256, 32); // 256-bit
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
        // must not block all readers under the write lock. Python
        // holds its RLock throughout, but C# readers would starve; last-writer-wins
        // on _loggedIn preserves the observable outcome.
        string curName;
        string curHash;
        SyncRoot.EnterReadLock();
        try { curName = Name; curHash = _passwordHash; }
        finally { SyncRoot.ExitReadLock(); }
        var hash = HashPassword(password, saltOverride);
        // Both comparisons always run: short-circuiting the constant-time
        // compare on a name mismatch would let a caller distinguish "wrong
        // name" from "wrong password" by timing.
        bool nameOk = string.Equals(curName, name, StringComparison.OrdinalIgnoreCase);
        bool hashOk = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(curHash));
        bool ok = nameOk && hashOk;
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
        => Create<Account>(name, password, saltOverride, existsCheck);

    /// <summary>
    /// Typed factory: runs the full create pipeline (hash, AtCreate, atomic register)
    /// for game-code <see cref="Account"/> subclasses (e.g. CustomAccount).
    /// </summary>
    public static T Create<T>(string name, string password, string? saltOverride = null, Func<string,bool>? existsCheck = null) where T : Account, new()
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Name and password must not be empty.");
        if (existsCheck is not null && existsCheck(name))
            throw new InvalidOperationException($"Account with this name ({name}) already exists.");
        var acc = new T();
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
        // contain a throwing AtCreate like GameObject.Create
        // (swallow+log), so the account still registers below. (Python
        // base_account.py:45 is unguarded, but this matches
        // the C# GameObject.Create containment.)
        try { acc.AtCreate(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Account.Create: " + logEx.Message, "Account"); }
        // Atomic register — mirrors add_object_unique for race safety (port of test_duplicate_create_race.py)
        ObjectRegistry.AddObjectUnique(acc, o => o is Account a && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase), $"Account with this name ({name}) already exists.");
        return acc;
    }

    public override (string Sql, object[] Params) GetSaveOps()
    {
        // Flag dance + post-release encode live in the shared converter core;
        // only the account DTO snapshot stays here.
        string json = Persistence.Converters.GameObjectDtoConverter.BuildSaveJson(this, ToDto, clearing: false);
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [Id, json]);
    }
    public override (string Sql, object[] Params) GetSaveOpsClearing()
    {
        string json = Persistence.Converters.GameObjectDtoConverter.BuildSaveJson(this, ToDto, clearing: true);
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [Id, json]);
    }

    // DTO extension: store account fields in Extra for persistence simplicity
    // Fix for test_persistence.py:227 logged_in not persisted — mirrors __getstate__ setting logged_in=False
    public override GameObjectDto ToDto()
    {
        var dto = base.ToDto();
        dto.Type = "account";
        // Snapshot fields under SyncRoot : concurrent SetPassword /
        // AddCharacter must not tear the checkpoint. Recursion-safe: the
        // checkpoint path holds the write lock and the lock supports it.
        // Stash account extras via Extra dictionary (JSON) — never persist logged_in true
        SyncRoot.EnterReadLock();
        try
        {
            dto.Extra["password"] = Persistence.JsonOptions.ToElement(_passwordHash);
            dto.Extra["characters"] = Persistence.JsonOptions.ToElement(_characters);
            dto.Extra["banReason"] = Persistence.JsonOptions.ToElement(_banReason);
        }
        finally { SyncRoot.ExitReadLock(); }
        dto.Extra["loggedIn"] = Persistence.JsonOptions.ToElement(false);
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
            acc._passwordHash = ReadExtraString(pw);
        }
        if (dto.Extra.TryGetValue("characters", out var ch) && ch.ValueKind == System.Text.Json.JsonValueKind.Array)
            acc._characters = ch.EnumerateArray().Select(e => e.GetInt32()).ToList();
        else if (!dto.Extra.ContainsKey("characters")) acc._characters = [];
        if (dto.Extra.TryGetValue("banReason", out var br))
        {
            acc._banReason = ReadExtraString(br);
        }
        if (dto.Extra.TryGetValue("loggedIn", out var li) && li.ValueKind == System.Text.Json.JsonValueKind.True) acc._loggedIn = true;
        else acc._loggedIn = false;
        acc.IsModified = wantModified;
        return acc;
    }

    private static string ReadExtraString(System.Text.Json.JsonElement el) => el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() ?? "" : el.GetRawText().Trim('"');
}
