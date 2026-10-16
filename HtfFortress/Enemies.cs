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
    // New kinds go on the end (the byte goes over the wire).
    public enum EnemyKind : byte { Runner, Charger, Spitter, Bomber, Tank, Boss, Thief, Shield, Digger, Healer, Splitter, Mini, Queen, Tide }

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
                case EnemyKind.Charger: return new EnemyStats { BaseHp = 15f, Speed = 1.8f, Hp = 0.5f, Damage = 0.6f, WallMult = 1f, Scale = 0.75f, Reward = 2, Gear = "Helmet", FirstWave = 3 };
                case EnemyKind.Spitter: return new EnemyStats { BaseHp = 40f, Speed = 0f, Hp = 1.2f, Damage = 1f, WallMult = 1f, Scale = 1.1f, Reward = 5, Gear = "SpitterCannon", FirstWave = 5 };
                case EnemyKind.Bomber: return new EnemyStats { BaseHp = 20f, Speed = 1.2f, Hp = 0.6f, Damage = 8f, WallMult = 1f, Scale = 1f, Reward = 4, Gear = "Bomb", FirstWave = 6 };
                case EnemyKind.Tank: return new EnemyStats { BaseHp = 150f, Speed = 0.5f, Hp = 5f, Damage = 2.5f, WallMult = 2f, Scale = 1.8f, Reward = 10, Gear = "Shell", FirstWave = 8 };
                case EnemyKind.Boss: return new EnemyStats { BaseHp = 700f, Speed = 0.45f, Hp = 40f, Damage = 6f, WallMult = 2f, Scale = 3.5f, Reward = 120, Gear = "BossShell", FirstWave = 10 };
                // Grabs a corpse (or coins at the crate), hops walls, runs for the sea: someone has to chase it.
                case EnemyKind.Thief: return new EnemyStats { BaseHp = 25f, Speed = 2.0f, Hp = 0.8f, Damage = 0.3f, WallMult = 1f, Scale = 0.8f, Reward = 6, Gear = "ThiefSack", FirstWave = 4 };
                // Its shield takes 80% of turret damage from the front; players and side turrets hit it full.
                case EnemyKind.Shield: return new EnemyStats { BaseHp = 60f, Speed = 0.8f, Hp = 2f, Damage = 1f, WallMult = 1f, Scale = 1.15f, Reward = 6, Gear = "ShieldBoard", FirstWave = 7 };
                // Tunnels under the first wall it meets.
                case EnemyKind.Digger: return new EnemyStats { BaseHp = 35f, Speed = 1.1f, Hp = 1.2f, Damage = 1f, WallMult = 1f, Scale = 0.9f, Reward = 5, Gear = "MinerHat", FirstWave = 9 };
                // Heals the fish around it.
                case EnemyKind.Healer: return new EnemyStats { BaseHp = 40f, Speed = 0.85f, Hp = 1.2f, Damage = 0.5f, WallMult = 1f, Scale = 0.95f, Reward = 7, Gear = "HealerStaff", FirstWave = 6 };
                // Bursts into three minis when it dies.
                case EnemyKind.Splitter: return new EnemyStats { BaseHp = 45f, Speed = 0.9f, Hp = 1.5f, Damage = 1f, WallMult = 1f, Scale = 1.25f, Reward = 4, Gear = "SplitterBubbles", FirstWave = 5 };
                case EnemyKind.Mini: return new EnemyStats { BaseHp = 8f, Speed = 1.6f, Hp = 0.3f, Damage = 0.4f, WallMult = 1f, Scale = 0.55f, Reward = 1, Gear = null, FirstWave = 999 };
                // Wave 20 boss: calls in minions.
                case EnemyKind.Queen: return new EnemyStats { BaseHp = 900f, Speed = 0.4f, Hp = 40f, Damage = 5f, WallMult = 2f, Scale = 3.0f, Reward = 160, Gear = "QueenCrown", FirstWave = 20 };
                // Wave 30 boss: sends a breaker crashing over one beach.
                case EnemyKind.Tide: return new EnemyStats { BaseHp = 1100f, Speed = 0.4f, Hp = 40f, Damage = 6f, WallMult = 2f, Scale = 3.2f, Reward = 200, Gear = "TideHorn", FirstWave = 30 };
                default: return new EnemyStats { BaseHp = 30f, Speed = 1f, Hp = 1f, Damage = 1f, WallMult = 1f, Scale = 1f, Reward = 3, Gear = null, FirstWave = 1 };
            }
        }

        public static bool IsBoss(EnemyKind k) => k == EnemyKind.Boss || k == EnemyKind.Queen || k == EnemyKind.Tide;

        // Every 10th wave a boss; they take turns: Behemoth, Coral Queen, Tidecaller.
        public static EnemyKind BossFor(int wave) => (wave / 10 % 3) == 2 ? EnemyKind.Queen : (wave / 10 % 3) == 0 ? EnemyKind.Tide : EnemyKind.Boss;

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
            DrawPlates(cam);
        }

        // Over elites, bosses and the special kinds: name and health bar; over anything hurt: just the bar.
        private readonly List<(RectTransform root, TMPro.TextMeshProUGUI name, Hud.Bar bar)> _plates = new List<(RectTransform, TMPro.TextMeshProUGUI, Hud.Bar)>();
        private readonly Dictionary<int, int> _seenMax = new Dictionary<int, int>();

        private static bool Named(EnemyTag t) => t.Elite || EnemyCatalog.IsBoss(t.Kind) || t.Kind >= EnemyKind.Thief && t.Kind != EnemyKind.Mini;

        private void DrawPlates(Camera cam)
        {
            int used = 0;
            var cm = InstanceFinder.ClientManager;
            if (cam && cm != null && Hud.Font && Siege.Instance && Siege.Instance.ViewActive)
                foreach (int id in _dressed)
                {
                    if (used >= 24) break;
                    if (!cm.Objects.Spawned.TryGetValue(id, out NetworkObject nob) || !nob || !(nob.GetComponent<Creature>() is Creature c) || !_clientTags.TryGetValue(id, out var tag)) continue;
                    int hp = c._hp.Value;
                    if (hp <= 0) continue;
                    int max = _seenMax.TryGetValue(id, out int m) ? Mathf.Max(m, hp) : hp;
                    _seenMax[id] = max;
                    bool named = Named(tag), hurt = hp < max;
                    float dist = Vector3.Distance(cam.transform.position, nob.transform.position);
                    if (!(named && dist < 35f) && !(hurt && dist < 22f)) continue;
                    var rends = nob.GetComponentsInChildren<Renderer>();
                    float top = rends.Length > 0 ? rends.Max(r => r.bounds.max.y) : nob.transform.position.y + 1f;
                    Vector3 sp = cam.WorldToScreenPoint(new Vector3(nob.transform.position.x, top + 0.35f, nob.transform.position.z));
                    if (sp.z < 0f) continue;
                    if (used >= _plates.Count)
                    {
                        var rt = Hud.Node(Hud.Root, "Plate", Vector2.zero, Vector2.zero, new Vector2(160f, 40f), new Vector2(0.5f, 0f));
                        var name = Hud.Label(rt, "", 18f, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(240f, 26f), TMPro.TextAlignmentOptions.Bottom, bold: true, outline: true);
                        var bar = Hud.Bar.Make(rt, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(74f, 9f), Hud.Bad);
                        _plates.Add((rt, name, bar));
                    }
                    var (root, label, hpBar) = _plates[used++];
                    root.gameObject.SetActive(true);
                    root.anchorMin = root.anchorMax = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
                    root.anchoredPosition = Vector2.zero;
                    float scale = Mathf.Clamp(14f / Mathf.Max(1f, dist), 0.65f, 1.15f) * (EnemyCatalog.IsBoss(tag.Kind) ? 1.4f : 1f);
                    root.localScale = Vector3.one * scale;
                    label.gameObject.SetActive(named);
                    if (named) { label.text = (tag.Elite ? "★ " : "") + L10n.T("enemy" + tag.Kind); label.color = tag.Elite || EnemyCatalog.IsBoss(tag.Kind) ? Hud.Gold : Color.Lerp(Color.white, DyeOf(tag.Kind), 0.8f); }
                    hpBar.Set((float)hp / Mathf.Max(1, max), Hud.Bad);
                }
            for (int i = used; i < _plates.Count; i++) if (_plates[i].root) _plates[i].root.gameObject.SetActive(false);
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

            _clientTags[tag.ObjectId] = tag;
            Tint(root, tag.Kind); // before the gear goes on, so only the fish is dyed
            if (stats.Gear != null) Wear(root, stats.Gear, topLocal - Vector3.up * (stats.Gear.EndsWith("Shell") ? 0.35f * b.size.y / stats.Scale : 0.05f), width / stats.Scale);
            if (tag.Elite) Wear(root, "Crown", topLocal, width * 0.7f / stats.Scale);
            _dressed.Add(tag.ObjectId);
            AutoTest.Count("dressed_" + tag.Kind);
            return true;
        }

        // ---------- look: a dye per kind (a tinted copy of the fish's palette texture) ----------

        private readonly Dictionary<int, EnemyTag> _clientTags = new Dictionary<int, EnemyTag>();
        private static readonly Dictionary<(Texture, EnemyKind), Texture> Tinted = new Dictionary<(Texture, EnemyKind), Texture>();
        private static Material _blit;

        public static Color DyeOf(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Charger: return new Color(1f, 0.7f, 0.66f);
                case EnemyKind.Bomber: return new Color(0.72f, 0.72f, 0.8f);
                case EnemyKind.Tank: return new Color(0.82f, 0.8f, 0.72f);
                case EnemyKind.Thief: return new Color(0.82f, 0.7f, 1f);
                case EnemyKind.Shield: return new Color(0.72f, 0.84f, 1f);
                case EnemyKind.Digger: return new Color(0.92f, 0.78f, 0.58f);
                case EnemyKind.Healer: return new Color(0.68f, 1f, 0.72f);
                case EnemyKind.Splitter: case EnemyKind.Mini: return new Color(0.84f, 1f, 0.52f);
                case EnemyKind.Queen: return new Color(1f, 0.74f, 0.84f);
                case EnemyKind.Tide: return new Color(0.62f, 0.84f, 1f);
                case EnemyKind.Boss: return new Color(0.78f, 0.74f, 0.7f);
                default: return Color.white;
            }
        }

        private static void Tint(Transform root, EnemyKind kind)
        {
            Color c = DyeOf(kind);
            if (c == Color.white) return;
            if (!_blit) { var sh = Shader.Find("UI/Default"); if (!sh) return; _blit = new Material(sh); }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials; // per-fish instances
                foreach (var m in mats)
                    foreach (string prop in new[] { "_Colors", "_BaseMap", "_MainTex" })
                    {
                        if (!m.HasProperty(prop) || !(m.GetTexture(prop) is Texture src)) continue;
                        if (!Tinted.TryGetValue((src, kind), out var dst))
                        {
                            var rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = src.filterMode, wrapMode = src.wrapMode };
                            _blit.color = c;
                            Graphics.Blit(src, rt, _blit);
                            Tinted[(src, kind)] = dst = rt;
                        }
                        m.SetTexture(prop, dst);
                        break;
                    }
                r.materials = mats;
            }
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
