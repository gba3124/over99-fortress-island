using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;

namespace HtfFortress
{
    // Both ways, once per connection: the build of the mod this machine runs. Never change this struct or its name: it is
    // how two different builds still recognise each other (FishNet keys a broadcast by its type name and skips unknown keys).
    public struct ModHello : IBroadcast { public string Build; }

    // Everyone must run the very same HtfFortress.dll: the mod's messages are read field by field, so a friend on another
    // build misreads them and the game goes wrong in odd ways. This says so up front, on the host's screen and on theirs.
    public class VersionCheck : MonoBehaviour
    {
        // Plugin version plus the first 6 hex digits of the DLL's SHA-256 (tools/pack.sh names its zip the same way).
        public static readonly string Build = MakeBuild();
        private const float AnswerWithin = 20f;

        private NetworkManager _registeredOn;
        private float _sentAt = -1f;
        private bool _answered, _warnedHost;
        private readonly Dictionary<int, string> _heard = new Dictionary<int, string>(); // host: client id -> its build
        private readonly Dictionary<int, float> _seen = new Dictionary<int, float>();    // host: client id -> when its player showed up
        private readonly HashSet<int> _warned = new HashSet<int>();

        static VersionCheck()
        {
            GenericWriter<ModHello>.SetWrite((w, v) => w.WriteString(v.Build ?? ""));
            GenericReader<ModHello>.SetRead(r => new ModHello { Build = r.ReadString() });
        }

        private static string MakeBuild()
        {
            var asm = typeof(VersionCheck).Assembly;
            try
            {
                using (var sha = SHA256.Create())
                    return FortressPlugin.Version + "-" + string.Concat(sha.ComputeHash(File.ReadAllBytes(asm.Location)).Take(3).Select(b => b.ToString("x2")));
            }
            catch { return FortressPlugin.Version + "-" + asm.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 6); }
        }

        private void Start() => BuildSystem.Log.LogInfo("HTF Fortress build " + Build);

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ServerManager.RegisterBroadcast<ModHello>((conn, h, ch) =>
                {
                    _heard[conn.ClientId] = h.Build;
                    InstanceFinder.ServerManager.Broadcast(conn, new ModHello { Build = Build }); // so they can check too
                    if (h.Build != Build) Warn(conn.ClientId, L10n.T("versionOther", NameOf(conn.ClientId), h.Build, Build));
                });
                nm.ClientManager.RegisterBroadcast<ModHello>((h, ch) =>
                {
                    _answered = true;
                    if (!InstanceFinder.IsServerStarted && h.Build != Build) Say(L10n.T("versionMine", Build, h.Build));
                });
                nm.ServerManager.OnRemoteConnectionState += (conn, a) =>
                {
                    if (a.ConnectionState != RemoteConnectionState.Stopped) return;
                    _heard.Remove(conn.ClientId); _seen.Remove(conn.ClientId); _warned.Remove(conn.ClientId);
                };
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) { _sentAt = -1f; _answered = _warnedHost = false; } };
            }
            if (InstanceFinder.IsServerStarted) HostChecks();
            else if (InstanceFinder.IsClientStarted)
            {
                // Once our player exists (we're in, and authenticated), tell the host which build we run.
                if (_sentAt < 0f && Player.LocalPlayer) { _sentAt = Time.time; InstanceFinder.ClientManager.Broadcast(new ModHello { Build = Build }); }
                if (_sentAt >= 0f && !_answered && !_warnedHost && Time.time - _sentAt > AnswerWithin) { _warnedHost = true; Say(L10n.T("versionHostNone")); }
            }
        }

        // A friend who never says hello has no fortress mod, or one from before this check.
        private void HostChecks()
        {
            foreach (var p in PlayerManager.Players)
            {
                if (!p || p.Owner == null || p.Owner.IsLocalClient) continue;
                int id = p.Owner.ClientId;
                if (!_seen.TryGetValue(id, out float since)) _seen[id] = since = Time.time;
                if (!_heard.ContainsKey(id) && Time.time - since > AnswerWithin) Warn(id, L10n.T("versionNone", Loot.NameOf(p)));
            }
        }

        private static string NameOf(int clientId)
        {
            var p = PlayerManager.Players.FirstOrDefault(x => x && x.Owner != null && x.Owner.ClientId == clientId);
            return p ? Loot.NameOf(p) : "Player " + clientId;
        }

        private void Warn(int clientId, string text) { if (_warned.Add(clientId)) Say(text); }

        private static void Say(string text)
        {
            BuildSystem.Log.LogWarning(text);
            Hud.Banner(text, 12f, Hud.Bad);
        }
    }

    // A server-side message handler that throws gets its sender kicked by FishNet (as malformed data): log it instead.
    internal static class Net
    {
        public static System.Action<NetworkConnection, T, Channel> Safe<T>(System.Action<NetworkConnection, T, Channel> handler) =>
            (conn, msg, ch) =>
            {
                try { handler(conn, msg, ch); }
                catch (System.Exception e) { BuildSystem.Log.LogError($"{typeof(T).Name} from client {conn.ClientId}: {e}"); }
            };
    }
}
