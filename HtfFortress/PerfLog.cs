using FishNet;
using UnityEngine;

namespace HtfFortress
{
    // Diagnostic for the playtest complaint "every few seconds on every client everything stopped and then jumped, only
    // the host was fine". A long frame on the host is exactly what the clients see as that freeze, so both sides log the
    // worst frame of each ten-second window together with what it cost (wave, how many enemies are on the field) and how
    // much memory the mod's runtime holds. Read it in BepInEx/LogOutput.log: "perf host: ..." / "perf client: ...".
    // It only logs, it never changes what the game does.
    public class PerfLog : MonoBehaviour
    {
        private float _window, _worst, _frames, _next;
        private long _worstAtMem;

        private void Update()
        {
            if (FortressPlugin.Instance == null || !FortressPlugin.Instance.VerboseLogs)
            {
                _window = _worst = 0f; _frames = 0; _next = Time.unscaledTime + 10f;
                return;
            }
            float dt = Time.unscaledDeltaTime;
            _window += dt;
            _frames++;
            if (dt > _worst) { _worst = dt; _worstAtMem = System.GC.GetTotalMemory(false) / 1024; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 10f;
            string role = InstanceFinder.IsServerStarted ? "host" : InstanceFinder.IsClientStarted ? "client" : "solo";
            int wave = Siege.Instance ? Siege.Instance.View.Wave : 0;
            bool host = InstanceFinder.IsServerStarted;
            int enemies = host ? FortressPlugin.Alive.Count : (Enemies.Instance ? Enemies.Instance.ClientCount : 0);
            int helpers = Helpers.Instance ? (host ? Helpers.Instance.DebugCount : Helpers.Instance.ViewCount) : 0;
            int drops = Loot.Instance ? Loot.Instance.ClientDropViews : 0, scales = Loot.Instance ? Loot.Instance.ClientScaleViews : 0;
            int pieces = BuildSystem.Instance ? BuildSystem.Instance.PieceCount : 0;
            float fps = _window > 0f && _frames > 0f ? _frames / _window : 0f;
            int fx = Fx.Queued; Fx.Queued = 0;
            int corpses = 0, items = 0, turrets = 0;
            if (InstanceFinder.IsServerStarted && FortressNet.Instance) { foreach (var _ in FortressNet.Instance.ServerCorpses()) corpses++; }
            if (ItemManager.Items != null) items = ItemManager.Items.Count;
            if (BuildSystem.Instance) foreach (var pc in BuildSystem.Instance.ServerPieces) if (Catalog.IsTurret(pc.Type)) turrets++;
            BuildSystem.Log.LogInfo($"perf {role}: {Mathf.RoundToInt(fps)} fps, worst frame {Mathf.RoundToInt(_worst * 1000f)} ms at {_worstAtMem} KB, wave {wave}, enemies {enemies}, helpers {helpers}, drops {drops}, scales {scales}, pieces {pieces}, fx {fx}/10s, corpses {corpses}, items {items}, turrets {turrets}");
            _window = 0f; _worst = 0f; _frames = 0;
        }
    }
}
