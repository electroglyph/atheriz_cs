using System.Security.Cryptography;
using System.Text;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

/// <summary>
/// Inherits GameObject flags (is_account) but adds Account-specific state.
/// </summary>
public class Account : GameObject
{
    public static bool GroupSave => false; // Fix for test_account.py:39

    private string _passwordHash = "";
    private List<int> _characters = [];
    private string _banReason = "";
    private bool _loggedIn;

    public Account()
    {
        IsAccount = true;
    }
    // Load-path construction: no id draw (the factory adopts the stored id
    // via the base core before publication, so the generator watermark is
    // untouched by loads).
    private Account(int id) : base(id)
    {
        IsAccount = true;
    }
    internal static new Account CreateForLoad(int id) => new Account(id);
    public override bool AtDelete(GameObject? caller)
    {
        // Unconditional true (test_account.py:88 — not access-gated like the base),
        // routed through the hook pipeline so game code can veto via at_delete hooks.
        return Hookable(HookName.AtDelete, () => true, caller);
    }
    public virtual bool AtPrePuppet(GameObject character) => Hookable(HookName.AtPrePuppet, () => true, character);
    // Account deletes immediately (single row, no recursive walk): virtual
    // dispatch so a GameObject-typed reference takes this path without the
    // base naming the derived type.
    public override (int Count, List<DeleteOperation> Operations)? Delete(GameObject? caller = null, bool recursive = false, int maxDepth = ContentUtils.DefaultMaxSearchDepth)
    {
        return DeleteImmediate(caller);
    }

    // Shared immediate-delete core for both static types .
    internal (int Count, List<DeleteOperation> Operations)? DeleteImmediate(GameObject? caller)
    {
        // DB write — the world lives in memory after startup and the DB is
        // written only on save checkpoints (mirrors Node.delete). The shared
        // teardown leaves no dangling follows, channel memberships,
        // sessions, or tick slots.
        if (!AtDelete(caller)) return null;
        // Mark deleted and unregister BEFORE journaling so a concurrent
        // checkpoint cannot resurrect the row. Mirrors Node.delete.
        SyncRoot.EnterWriteLock();
        try { IsDeleted = true; } finally { SyncRoot.ExitWriteLock(); }
        if (!IsTemporary) ObjectRegistry.NoteDeleted(Id);
        ObjectRegistry.RemoveObject(this);
        TeardownDeleted(this);
        return (1, []);
    }

    public string PasswordHash
    {
        get => Read(() => _passwordHash);
        private set => Write(() => { _passwordHash = value; IsModified = true; });
    }
    public IReadOnlyList<int> Characters => Read(() => (IReadOnlyList<int>)new List<int>(_characters));
    public override string BanReason { get => Read(() => _banReason); set => Write(() => { _banReason = value; IsModified = true; }); }

    /// <inheritdoc/>
    public override bool IsKnownProperty(string name) => name switch
    {
        "Characters" or "characters" or "_characters" => true,
        "BanReason" or "ban_reason" or "_ban_reason" => true,
        "PasswordHash" or "password_hash" or "_password_hash" => true,
        "LoggedIn" or "logged_in" or "_logged_in" => true,
        _ => base.IsKnownProperty(name),
    };

    /// <inheritdoc/>
    public override bool TrySetProperty(string name, object? value, out string? error)
    {
        error = null;
        switch (name)
        {
            case "BanReason" or "ban_reason" or "_ban_reason":
                // Null passes into the non-nullable property like the old
                // (string) cast did; the compiler cannot see that flow.
                BanReason = ToText(value)!;
                return true;
            case "Characters" or "characters" or "_characters"
                or "PasswordHash" or "password_hash" or "_password_hash"
                or "LoggedIn" or "logged_in" or "_logged_in":
                error = $"'{name}' is a read-only attribute.";
                return false;
            default:
                return base.TrySetProperty(name, value, out error);
        }
    }
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

    // Atomic verify-and-set for future change-password flows: the
    // split CheckPassword-then-SetPassword lets an interleaved rotation get
    // blindly overwritten. Hashes outside the lock (PBKDF2 is ~100ms);
    // the write lock only commits when the stored hash still matches the
    // verified generation. Contrast Login's under-write-lock re-verify.
    public bool ChangePassword(string currentPassword, string newPassword, string? saltOverride = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);
        string currentHash = HashPassword(currentPassword, saltOverride);
        string newHash = HashPassword(newPassword, saltOverride);
        SyncRoot.EnterWriteLock();
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(currentHash), Encoding.UTF8.GetBytes(_passwordHash)))
                return false;
            _passwordHash = newHash;
            IsModified = true;
            return true;
        }
        finally { SyncRoot.ExitWriteLock(); }
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
        try
        {
            // The snapshot above can go stale while PBKDF2 runs: a rotation
            // in that window must not log in with the OLD password (D1), and a
            // rename in that window must not log in under the OLD name.
            // Re-read under the write lock; when either the stored hash or the
            // name moved, re-verify the already-computed candidate against the
            // CURRENT values (no second PBKDF2 needed — same salt, same algorithm).
            if (!string.Equals(curName, Name, StringComparison.OrdinalIgnoreCase)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(curHash), Encoding.UTF8.GetBytes(_passwordHash)))
            {
                nameOk = string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);
                hashOk = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(_passwordHash));
                ok = nameOk && hashOk;
            }
            _loggedIn = ok;
        }
        finally { SyncRoot.ExitWriteLock(); }
        return ok;
    }

    public void AddCharacter(GameObject character)
    {
        SyncRoot.EnterWriteLock();
        try { if (!_characters.Contains(character.Id)) { _characters.Add(character.Id); IsModified = true; } }
        finally { SyncRoot.ExitWriteLock(); }
    }
    // Atomic limit-aware reserve for character creation: the old
    // check (Count >= MaxCharacters at the call site) then AddCharacter
    // let two concurrent creates both pass and exceed the cap.
    // Returns false when the cap is already reached (character not added).
    public bool TryAddCharacter(GameObject character, int maxCharacters)
    {
        SyncRoot.EnterWriteLock();
        try
        {
            if (_characters.Contains(character.Id)) return true;
            if (_characters.Count >= maxCharacters) return false;
            _characters.Add(character.Id); IsModified = true;
            return true;
        }
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
        ObjectRegistry.AddObjectUnique(acc, o => o is Account a && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase), $"Account with this name ({name}) already exists.");
        return acc;
    }

    public override SaveOperation GetSaveOperation()
    {
        // Flag dance + post-release encode live in the shared converter core;
        // only the account DTO snapshot stays here.
        string json = Persistence.Converters.GameObjectDtoConverter.BuildSaveJson(this, ToDto, clearing: false);
        return new SaveOperation(Id, json);
    }
    public override SaveOperation GetSaveOperationClearing()
    {
        string json = Persistence.Converters.GameObjectDtoConverter.BuildSaveJson(this, ToDto, clearing: true);
        return new SaveOperation(Id, json);
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
        var acc = Account.CreateForLoad(dto.Id);
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
        {
            // One corrupt entry must not abort the whole account load:
            // keep the valid ids, skip the rest.
            var list = new List<int>();
            foreach (var e in ch.EnumerateArray())
            {
                if (e.ValueKind == System.Text.Json.JsonValueKind.Number && e.TryGetInt32(out var v))
                    list.Add(v);
            }
            acc._characters = list;
        }
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
