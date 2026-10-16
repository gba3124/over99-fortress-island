using System.Collections.Generic;
using BepInEx.Configuration;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using UnityEngine;

namespace HtfFortress
{
    // Turrets pick the closest wave creature in range on the host and damage it with the game's own ServerChangeHp.
    // Visuals go through Fx (tracer for the gun, a lobbed ball + blast for the cannon) so every client sees them.
    public class Turrets : MonoBehaviour
    {
        public static ConfigEntry<float> DamageMultiplier;

        private readonly Dictionary<int, float> _nextFire = new Dictionary<int, float>();

        private void Update()
        {
            if (InstanceFinder.IsServerStarted && Siege.Instance.ServerActive) ServerFire();
        }

        // ---------- host ----------

        private void ServerFire()
        {
            var alive = FortressPlugin.Alive;
            if (alive.Count == 0) return;
            foreach (var p in BuildSystem.Instance.ServerPieces)
            {
                if (!Catalog.IsTurret(p.Type)) continue;
                _nextFire.TryGetValue(p.Id, out float next);
                if (Time.time < next) continue;

                var stats = Catalog.Turret(p.Type, p.Level);
                Vector3 muzzle = p.Pos + Vector3.up * 1.1f;
                Creature target = null;
                float best = stats.Range * stats.Range;
                foreach (var c in alive)
                {
                    if (!c || c._hp.Value <= 0) continue;
                    float d = (c.transform.position - muzzle).sqrMagnitude;
                    if (d < best) { best = d; target = c; }
                }
                if (!target) continue;

                _nextFire[p.Id] = Time.time + stats.Interval;
                int dmg = Mathf.Max(1, Mathf.RoundToInt(stats.Damage * DamageMultiplier.Value));
                Vector3 hit = target.transform.position;
                if (stats.Splash > 0f)
                {
                    float r2 = stats.Splash * stats.Splash;
                    foreach (var c in alive)
                        if (c && c._hp.Value > 0 && (c.transform.position - hit).sqrMagnitude <= r2) c.ServerChangeHp(dmg);
                }
                else target.ServerChangeHp(dmg);

                AutoTest.Count("shots_" + p.Type);
                Fx.Send(p.Type == PieceType.Cannon ? FxKind.Cannonball : FxKind.Tracer, muzzle, hit, p.Id);
            }
        }
    }
}
