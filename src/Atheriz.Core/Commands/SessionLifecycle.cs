using Atheriz.Core.Network;

namespace Atheriz.Core.Commands.LoggedIn
{
    // Shared quiet-close behind the quit/shutdown/reload verbs, resolved through
    // the IMessageTarget surface: a raw connection closes itself, any other
    // caller with a session closes that session's connection. Session-less shapes
    // carry the interface defaults (null session) so they stay a silent noop.
    // "Goodbye!" delivery stays at the call site (message before close).
    internal static class ConnectionHelper
    {
        internal static void CloseQuietly(IMessageTarget caller)
        {
            try
            {
                if (caller is BaseConnection)
                    caller.Close();
                else
                    caller.Session?.Connection?.Close();
            }
            catch (Exception) { }
        }
    }
}

namespace Atheriz.Core.Commands.UnloggedIn
{
    // Shared preamble for create.py / guest.py / new.py: cooldown reserve/clear/apply
    // plus the atomic session-puppet attach (also used a 4th time by ConnectCommand).
    public static class CreationCooldownHelper
    {
        // Owner tokens per caller-identity: a validation failure clears
        // only its own reservation — never another drain's live hold on the
        // same host key. Keyed by connection session (unique per drain), with
        // an identity-hash fallback for non-connection callers.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> _owners = new();
        private static string OwnerKey(IMessageTarget caller)
        {
            if (caller is BaseConnection bc && bc.SessionId is not null) return "conn:" + bc.SessionId;
            if (caller is null) return "?";
            return "obj:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(caller);
        }
        public static string RateKey(IMessageTarget caller)
        {
// host string when available, else id(caller).
            // Non-connection callers get identity keys : sharing the
            // "?" bucket bypassed throttling entirely via the early-true in
            // TryReserveCreationCooldown. Identity (not Id-hash) matches
            // Python id() and survives same-Id reloads without stale buckets.
            if (caller is BaseConnection bc)
            {
                string host = bc.ClientHost ?? "?";
                if (!string.IsNullOrEmpty(host) && host != "?") return host;
            }
            if (caller is null) return "?";
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(caller).ToString();
        }

        // Mirrors try_reserve_creation_cooldown: messages + false when rate-limited.
        public static bool TryReserve(IMessageTarget caller, string kind)
        {
            var settings = Settings.AtherizSettings.Global;
            double now = Utils.GameClock.MonotonicSeconds();
            var token = ObjectRegistry.TryReserveCreationCooldown(kind, RateKey(caller), now, settings.CreationCooldown);
            if (token is null)
            { caller.Msg("Creation is temporarily rate-limited. Please try again later."); return false; }
            _owners[OwnerKey(caller)] = token.Value;
            return true;
        }

        // Mirrors clear_creation_cooldown on every validation-failure return.
        public static void Clear(IMessageTarget caller)
        {
            if (_owners.TryRemove(OwnerKey(caller), out var token))
                ObjectRegistry.ClearCreationCooldown(RateKey(caller), token);
        }

        // Mirrors apply_creation_cooldown on success.
        public static void Apply(IMessageTarget caller, string kind)
        {
            var settings = Settings.AtherizSettings.Global;
            ObjectRegistry.ApplyCreationCooldown(kind, RateKey(caller), Utils.GameClock.MonotonicSeconds(), settings.CreationCooldown);
            _owners.TryRemove(OwnerKey(caller), out _);
        }
    }
}
