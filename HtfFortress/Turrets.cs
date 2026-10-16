using System.Collections.Generic;
using BepInEx.Configuration;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using UnityEngine;

namespace HtfFortress
{
    // Host -> everyone: a turret fired from `From` at `To`, so every screen can draw it.
    public struct ShotFx : IBroadcast { public int PieceId; public Vector3 From; public Vector3 To; public bool Cannon; }

    // Turrets pick the closest wave creature in range on the host and damage it with the game's own ServerChangeHp.
    // Clients turn the turret head and draw a tracer (gun) or a flying ball + blast (cannon).
    public class Turrets : MonoBehaviour
    {
        public static ConfigEntry<float> DamageMultiplier;

        private readonly Dictionary<int, float> _nextFire = new Dictionary<int, float>();
        private NetworkManager _registeredOn;
        private Material _tracerMat, _ballMat, _blastMat;

        static Turrets()
        {
            GenericWriter<ShotFx>.SetWrite((w, v) => { w.WriteInt32(v.PieceId); w.WriteVector3(v.From); w.WriteVector3(v.To); w.WriteBoolean(v.Cannon); });
            GenericReader<ShotFx>.SetRead(r => new ShotFx { PieceId = r.ReadInt32(), From = r.ReadVector3(), To = r.ReadVector3(), Cannon = r.ReadBoolean() });
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<ShotFx>((fx, ch) => Play(fx));
            }
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

                InstanceFinder.ServerManager.Broadcast(new ShotFx { PieceId = p.Id, From = muzzle, To = hit, Cannon = p.Type == PieceType.Cannon });
            }
        }

        // ---------- every client ----------

        private void Play(ShotFx fx)
        {
            var piece = BuildSystem.Instance.PieceObject(fx.PieceId);
            var head = piece ? piece.transform.Find("Head") : null;
            if (head)
            {
                Vector3 dir = fx.To - head.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) head.rotation = Quaternion.LookRotation(dir);
            }
            if (!_tracerMat)
            {
                _tracerMat = Models.Make(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.7f, 0.2f));
                _ballMat = Models.Make(new Color(0.15f, 0.15f, 0.15f));
                _blastMat = Models.Make(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.5f, 0.1f));
            }
            if (fx.Cannon) StartCoroutine(Cannonball(fx.From, fx.To));
            else StartCoroutine(Tracer(fx.From, fx.To));
        }

        private static GameObject Shape(PrimitiveType type, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private System.Collections.IEnumerator Tracer(Vector3 from, Vector3 to)
        {
            var go = Shape(PrimitiveType.Cube, _tracerMat);
            Vector3 d = to - from;
            go.transform.SetPositionAndRotation(from + d * 0.5f, Quaternion.LookRotation(d));
            go.transform.localScale = new Vector3(0.05f, 0.05f, d.magnitude);
            yield return new WaitForSeconds(0.06f);
            Destroy(go);
        }

        private System.Collections.IEnumerator Cannonball(Vector3 from, Vector3 to)
        {
            var ball = Shape(PrimitiveType.Sphere, _ballMat);
            ball.transform.localScale = Vector3.one * 0.35f;
            const float flight = 0.35f;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                float k = t / flight;
                ball.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.5f;
                yield return null;
            }
            Destroy(ball);
            var blast = Shape(PrimitiveType.Sphere, _blastMat);
            blast.transform.position = to;
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                blast.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 6f, t / 0.25f);
                yield return null;
            }
            Destroy(blast);
        }
    }
}
