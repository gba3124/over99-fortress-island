using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;

namespace HtfFortress
{
    public enum EnemyKind : byte { Runner, Charger, Spitter, Bomber, Tank, Boss }

    // Host -> everyone: what a spawned walker is, so every client scales it and adds its gear.
    public struct EnemyTag : IBroadcast { public int ObjectId; public EnemyKind Kind; public bool Elite; }

    public struct EnemyStats
    {
        public float Speed, Hp, Damage, WallMult, Scale;
        public float BaseHp; // absolute health at wave 1 (the game's own fish health is far too high for a tower defence)
        public int Reward;
        public string Gear;
        public int FirstWave;
    }

    // All the fortress enemies are the game's walking fish ("dripper") driven by our own AI (see Siege.Steer),
    // told apart by size and Blender gear. Balance numbers live here.
    public static class EnemyCatalog
    {
        public static EnemyStats Of(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Charger: return new EnemyStats { BaseHp = 15f, Speed = 1.8f, Hp = 0.5f, Damage = 0.6f, WallMult = 1f, Scale = 0.75f, Reward = 2, Gear = null, FirstWave = 3 };
                case EnemyKind.Spitter: return new EnemyStats { BaseHp = 40f, Speed = 0f, Hp = 1.2f, Damage = 1f, WallMult = 1f, Scale = 1.1f, Reward = 5, Gear = "SpitterCannon", FirstWave = 5 };
                case EnemyKind.Bomber: return new EnemyStats { BaseHp = 20f, Speed = 1.2f, Hp = 0.6f, Damage = 8f, WallMult = 1f, Scale = 1f, Reward = 4, Gear = "Bomb", FirstWave = 6 };
                case EnemyKind.Tank: return new EnemyStats { BaseHp = 150f, Speed = 0.5f, Hp = 5f, Damage = 2.5f, WallMult = 2f, Scale = 1.8f, Reward = 10, Gear = "Shell", FirstWave = 8 };
                case EnemyKind.Boss: return new EnemyStats { BaseHp = 700f, Speed = 0.45f, Hp = 40f, Damage = 6f, WallMult = 2f, Scale = 3.5f, Reward = 120, Gear = "Shell", FirstWave = 10 };
                default: return new EnemyStats { BaseHp = 30f, Speed = 1f, Hp = 1f, Damage = 1f, WallMult = 1f, Scale = 1f, Reward = 3, Gear = null, FirstWave = 1 };
            }
        }

        public const float EliteHp = 3f, EliteDamage = 1.5f, EliteScale = 1.3f;
        public const int EliteReward = 3;
    }

    public class Enemies : MonoBehaviour
    {
        public static Enemies Instance;

        // host
        private readonly Dictionary<Creature, EnemyTag> _tags = new Dictionary<Creature, EnemyTag>();
        // clients: tags waiting for their object to show up
        private readonly List<(EnemyTag tag, float until)> _pending = new List<(EnemyTag, float)>();
        private readonly HashSet<int> _dressed = new HashSet<int>();
        private NetworkManager _registeredOn;

        static Enemies()
        {
            GenericWriter<EnemyTag>.SetWrite((w, v) => { w.WriteInt32(v.ObjectId); w.WriteByte((byte)v.Kind); w.WriteBoolean(v.Elite); });
            GenericReader<EnemyTag>.SetRead(r => new EnemyTag { ObjectId = r.ReadInt32(), Kind = (EnemyKind)r.ReadByte(), Elite = r.ReadBoolean() });
        }

        private void Awake() => Instance = this;

        public bool TryGet(Creature c, out EnemyTag tag) => _tags.TryGetValue(c, out tag);

        public static EnemyStats StatsOf(EnemyTag t)
        {
            var s = EnemyCatalog.Of(t.Kind);
            if (t.Elite) { s.BaseHp *= EnemyCatalog.EliteHp; s.Hp *= EnemyCatalog.EliteHp; s.Damage *= EnemyCatalog.EliteDamage; s.Scale *= EnemyCatalog.EliteScale; s.Reward *= EnemyCatalog.EliteReward; }
            return s;
        }

        // Host: call right after Server.Instance.Spawn.
        public void ServerTag(Creature c, EnemyKind kind, bool elite)
        {
            var tag = new EnemyTag { ObjectId = c.ObjectId, Kind = kind, Elite = elite };
            _tags[c] = tag;
            InstanceFinder.ServerManager.Broadcast(tag);
        }

        public void ServerForget(Creature c) => _tags.Remove(c);
        public void ServerClear() => _tags.Clear();
        public IEnumerable<KeyValuePair<Creature, EnemyTag>> ServerAll => _tags.Where(kv => kv.Key && kv.Key._hp.Value > 0);

        // Client: tagged enemies currently spawned here (for the HUD markers).
        public IEnumerable<Transform> ClientEnemies()
        {
            var cm = InstanceFinder.ClientManager;
            if (cm == null) yield break;
            foreach (int id in _dressed)
                if (cm.Objects.Spawned.TryGetValue(id, out NetworkObject nob) && nob && nob.GetComponent<Creature>() is Creature c && c.Hp > 0)
                    yield return nob.transform;
        }

        // Edge-of-screen markers for enemies you can't see (within 70 m).
        private readonly List<UnityEngine.UI.Image> _markers = new List<UnityEngine.UI.Image>();

        private void LateUpdate()
        {
            int used = 0;
            var cam = GameInfo.CurCamera;
            if (cam && Siege.Instance && Siege.Instance.ViewActive)
            {
                Vector3 me = cam.transform.position;
                foreach (var t in ClientEnemies())
                {
                    if (used >= 12) break;
                    Vector3 wp = t.position + Vector3.up;
                    float dist = Vector3.Distance(wp, me);
                    if (dist > 70f) continue;
                    Vector3 vp = cam.WorldToViewportPoint(wp);
                    bool onScreen = vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f;
                    if (onScreen) continue;
                    Vector2 d = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                    if (vp.z < 0f) d = -d;
                    if (d.sqrMagnitude < 0.0001f) d = Vector2.down;
                    d = d.normalized;
                    if (used >= _markers.Count) _markers.Add(Hud.CreateMarker("EnemyMarker", new Color(1f, 0.25f, 0.2f, 0.9f), 34f));
                    var m = _markers[used++];
                    m.gameObject.SetActive(true);
                    float s = Mathf.Max(Mathf.Abs(d.x) / 0.46f, Mathf.Abs(d.y) / 0.44f);
                    m.rectTransform.anchorMin = m.rectTransform.anchorMax = new Vector2(0.5f + d.x / s, 0.5f + d.y / s);
                    m.rectTransform.anchoredPosition = Vector2.zero;
                    m.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 180f); // the game's arrow sprite points left
                    m.color = new Color(1f, 0.25f, 0.2f, Mathf.Lerp(1f, 0.35f, dist / 70f));
                }
            }
            for (int i = used; i < _markers.Count; i++) if (_markers[i]) _markers[i].gameObject.SetActive(false);
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<EnemyTag>((t, ch) => _pending.Add((t, Time.time + 5f)));
                nm.SceneManager.OnClientLoadedStartScenes += (conn, asServer) =>
                {
                    if (!asServer) return;
                    foreach (var kv in _tags) if (kv.Key) InstanceFinder.ServerManager.Broadcast(conn, kv.Value);
                };
            }
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var (tag, until) = _pending[i];
                if (TryDress(tag) || Time.time > until) _pending.RemoveAt(i);
            }
        }

        // Client: scale the fish and give it its gear. The tag can arrive a frame before the object spawns, so it is retried.
        private bool TryDress(EnemyTag tag)
        {
            if (_dressed.Contains(tag.ObjectId)) return true;
            var cm = InstanceFinder.ClientManager;
            if (cm == null || !cm.Objects.Spawned.TryGetValue(tag.ObjectId, out NetworkObject nob) || !nob) return false;
            var stats = StatsOf(tag);
            var root = nob.transform;
            root.localScale = Vector3.one * stats.Scale;

            // Fit gear to the fish: sit on top of its renderers, sized to its width.
            var rends = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(root.position, Vector3.one);
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float width = Mathf.Max(0.3f, Mathf.Min(b.size.x, b.size.z));
            Vector3 topLocal = root.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));

            if (stats.Gear != null) Wear(root, stats.Gear, topLocal - Vector3.up * (stats.Gear == "Shell" ? 0.35f * b.size.y / stats.Scale : 0.05f), width / stats.Scale);
            if (tag.Elite) Wear(root, "Crown", topLocal, width * 0.7f / stats.Scale);
            _dressed.Add(tag.ObjectId);
            AutoTest.Count("dressed_" + tag.Kind);
            return true;
        }

        private static void Wear(Transform root, string model, Vector3 localPos, float size)
        {
            var holder = new GameObject(model + "Gear");
            holder.transform.SetParent(root, false);
            holder.transform.localPosition = localPos;
            holder.transform.localScale = Vector3.one * size;
            var go = Models.Attach(model, holder.transform, Vector3.one * 0.5f);
            go.layer = root.gameObject.layer;
        }
    }
}
