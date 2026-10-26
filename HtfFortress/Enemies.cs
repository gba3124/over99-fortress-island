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
    // The byte goes over the wire (host and friends run the same mod). From Warden on: the ten bosses in the order they
    // come (one every tenth wave), then what they summon. All of those are puppets: the game's creatures moved by us (Bosses).
    public enum EnemyKind : byte { Runner, Charger, Spitter, Bomber, Tank, Thief, Shield, Digger, Healer, Splitter, Mini,
                                   Warden, Urchin, Angler, Steed, Stonefish, Skyfish, Kraken, Voxel, Koi, Eye,
                                   Squadron, Glitch }

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
                case EnemyKind.Bomber: return new EnemyStats { BaseHp = 40f, Speed = 1.2f, Hp = 1.1f, Damage = 8f, WallMult = 1f, Scale = 1f, Reward = 4, Gear = "Bomb", FirstWave = 6 };
                case EnemyKind.Tank: return new EnemyStats { BaseHp = 150f, Speed = 0.5f, Hp = 5f, Damage = 2.5f, WallMult = 2f, Scale = 1.8f, Reward = 10, Gear = "Shell", FirstWave = 8 };
                // Grabs a corpse (or coins at the fish stall), hops walls, runs for the sea: someone has to chase it.
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
                // The bosses (BossBrains.cs): health at wave 1 (times the wave's health like everything), size (times the creature).
                case EnemyKind.Warden: return Boss(650f, 18f, 120);
                case EnemyKind.Urchin: return Boss(750f, 22f, 160);
                case EnemyKind.Angler: return Boss(900f, 10f, 200);
                case EnemyKind.Steed: return Boss(950f, 4.2f, 240); // 2.4 looked small next to the others (the playtest: 「真小啊」)
                case EnemyKind.Stonefish: return Boss(1000f, 9f, 280);
                case EnemyKind.Skyfish: return Boss(1050f, 11f, 320);
                case EnemyKind.Kraken: return Boss(1150f, 14f, 360);
                case EnemyKind.Voxel: return Boss(1200f, 10f, 420);
                case EnemyKind.Koi: return Boss(1300f, 12f, 500);
                case EnemyKind.Eye: return Boss(1600f, 95f, 1000);
                // What they summon: the flying fish's squadron, the voxel fish's copies.
                case EnemyKind.Squadron: return new EnemyStats { BaseHp = 14f, Speed = 1f, Hp = 0.5f, Damage = 1f, WallMult = 1f, Scale = 3f, Reward = 3, Gear = null, FirstWave = 999 };
                case EnemyKind.Glitch: return new EnemyStats { BaseHp = 40f, Speed = 1f, Hp = 1f, Damage = 1f, WallMult = 1f, Scale = 6f, Reward = 4, Gear = null, FirstWave = 999 };
                default: return new EnemyStats { BaseHp = 30f, Speed = 1f, Hp = 1f, Damage = 1f, WallMult = 1f, Scale = 1f, Reward = 3, Gear = null, FirstWave = 1 };
            }
        }

        private static EnemyStats Boss(float baseHp, float scale, int reward) =>
            new EnemyStats { BaseHp = baseHp, Speed = 1f, Hp = 40f, Damage = 1f, WallMult = 2f, Scale = scale, Reward = reward, Gear = null, FirstWave = 999 };

        public static bool IsBoss(EnemyKind k) => k >= EnemyKind.Warden && k <= EnemyKind.Eye;
        public static bool IsPuppet(EnemyKind k) => k >= EnemyKind.Warden; // moved by Bosses, not by the game or Siege

        // Where a boss turns up: on a beach, out of the sea off one, or in the air over one.
        public enum Body { Land, Sea, Sky }
        public static Body BodyOf(EnemyKind k) =>
            k == EnemyKind.Kraken ? Body.Sea
            : k == EnemyKind.Angler || k == EnemyKind.Skyfish || k == EnemyKind.Voxel || k == EnemyKind.Koi || k == EnemyKind.Eye || k == EnemyKind.Squadron || k == EnemyKind.Glitch ? Body.Sky
            : Body.Land;

        // OVER 99: a hundred waves, a boss every 10th, never the same one twice. Past 100 (testing, practice) they come
        // round again.
        public const int FinalWave = 100;
        public static readonly EnemyKind[] BossOrder =
            { EnemyKind.Warden, EnemyKind.Urchin, EnemyKind.Angler, EnemyKind.Steed, EnemyKind.Stonefish,
              EnemyKind.Skyfish, EnemyKind.Kraken, EnemyKind.Voxel, EnemyKind.Koi, EnemyKind.Eye };
        public static EnemyKind BossFor(int wave) => BossOrder[(Mathf.Max(1, wave / 10) - 1) % BossOrder.Length];

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
        private readonly Dictionary<int, Renderer[]> _rends = new Dictionary<int, Renderer[]>();
        private float _nextLod;
        private NetworkManager _registeredOn;

        static Enemies()
        {
            GenericWriter<EnemyTag>.SetWrite((w, v) => { w.WriteInt32(v.ObjectId); w.WriteByte((byte)v.Kind); w.WriteBoolean(v.Elite); });
            GenericReader<EnemyTag>.SetRead(r => new EnemyTag { ObjectId = r.ReadInt32(), Kind = (EnemyKind)r.ReadByte(), Elite = r.ReadBoolean() });
        }

        private void Awake() => Instance = this;

        public bool TryGet(Creature c, out EnemyTag tag) => _tags.TryGetValue(c, out tag);
        public int ClientCount => _clientTags.Count; // client: fortress fish on this screen right now
        public bool ClientIsFortress(int objectId) => _clientTags.ContainsKey(objectId); // client: one of the host's fortress fish
        public EnemyTag? ClientTagOf(int objectId) => _clientTags.TryGetValue(objectId, out var t) ? t : (EnemyTag?)null;

        // Client: a fortress enemy of this kind that's here now (the boss on stage for its entrance).
        public Transform ClientFind(EnemyKind kind)
        {
            var cm = InstanceFinder.ClientManager;
            if (cm == null) return null;
            foreach (var kv in _clientTags)
                if (kv.Value.Kind == kind && cm.Objects.Spawned.TryGetValue(kv.Key, out NetworkObject nob) && nob && !(nob.GetComponent<Creature>() is Creature c && c.IsDead))
                    return nob.transform;
            return null;
        }

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

        // For the tips: corpses near you, and which kinds are around.
        public bool ClientCorpseNear(Vector3 pos, float r)
        {
            var cm = InstanceFinder.ClientManager;
            if (cm == null) return false;
            foreach (int id in _dressed)
                if (cm.Objects.Spawned.TryGetValue(id, out NetworkObject nob) && nob && nob.GetComponent<Creature>() is Creature c && c._hp.Value <= 0
                    && (nob.transform.position - pos).sqrMagnitude < r * r) return true;
            return false;
        }

        public IEnumerable<EnemyKind> ClientKindsNear(Vector3 pos, float r)
        {
            var cm = InstanceFinder.ClientManager;
            if (cm == null) yield break;
            foreach (int id in _dressed)
                if (_clientTags.TryGetValue(id, out var t) && cm.Objects.Spawned.TryGetValue(id, out NetworkObject nob) && nob
                    && nob.GetComponent<Creature>() is Creature c && c._hp.Value > 0 && (nob.transform.position - pos).sqrMagnitude < r * r)
                    yield return t.Kind;
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
        private readonly Dictionary<int, (float above, float next)> _tops = new Dictionary<int, (float, float)>();

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
                    // Its top over its feet, looked up once a second (every renderer's bounds, for 24 plates every frame, added up).
                    if (!_tops.TryGetValue(id, out var top) || Time.time >= top.next)
                    {
                        var rends = nob.GetComponentsInChildren<Renderer>().Where(r => !r.forceRenderingOff && !r.GetComponentInParent<BossGear>()).ToArray(); // (not the Eye's turning corona: the plate would hop)
                        top = (rends.Length > 0 ? rends.Max(r => r.bounds.max.y) - nob.transform.position.y : 1f, Time.time + 1f);
                        _tops[id] = top;
                    }
                    Vector3 sp = cam.WorldToScreenPoint(nob.transform.position + Vector3.up * (top.above + 0.35f));
                    if (sp.z < 0f || sp.x < -40f || sp.x > Screen.width + 40f || sp.y < -40f || sp.y > Screen.height + 40f) continue; // no half-clipped plates at the screen edge
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
                    if (named)
                    {
                        string want = (tag.Elite ? "★ " : "") + L10n.T("enemy" + tag.Kind);
                        if (label.text != want) label.text = want;
                        Color wantColor = tag.Elite || EnemyCatalog.IsBoss(tag.Kind) ? Hud.Gold : Color.Lerp(Color.white, DyeOf(tag.Kind), 0.8f);
                        if (label.color != wantColor) label.color = wantColor;
                    }
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
            if (Time.unscaledTime >= _nextLod) { _nextLod = Time.unscaledTime + 0.35f; ApplyRenderLod(); }
            // Forget fish that are gone (the lists are walked every frame for markers, plates and tips).
            var cm = InstanceFinder.ClientManager;
            if (cm != null && Time.time >= _nextPrune) Prune(cm);
        }
        private float _nextPrune;

        // (Apart from Update: the lambda captures `cm`, and its closure was built every frame, not every 5 s.)
        private void Prune(FishNet.Managing.Client.ClientManager cm)
        {
            _nextPrune = Time.time + 5f;
            _dressed.RemoveWhere(id => !cm.Objects.Spawned.ContainsKey(id));
            foreach (int id in _clientTags.Keys.Where(id => !_dressed.Contains(id)).ToList()) { _clientTags.Remove(id); _seenMax.Remove(id); _tops.Remove(id); _rends.Remove(id); }
        }

        // Render LOD: 400 trash fish behind you still cost draw calls and skinned-mesh work. Only bosses and fish within
        // 90 m of the camera are drawn; the host's simulation is untouched.
        private void ApplyRenderLod()
        {
            var cam = GameInfo.CurCamera;
            if (!cam || _rends.Count == 0 || InstanceFinder.ClientManager == null) return;
            Vector3 eye = cam.transform.position;
            foreach (int id in _dressed)
            {
                if (!_rends.TryGetValue(id, out var rs) || rs == null || rs.Length == 0) continue;
                if (_clientTags.TryGetValue(id, out var tag) && EnemyCatalog.IsBoss(tag.Kind)) continue; // bosses are never hidden
                if (!InstanceFinder.ClientManager.Objects.Spawned.TryGetValue(id, out var nob) || !nob) continue;
                bool show = (nob.transform.position - eye).sqrMagnitude < 90f * 90f;
                for (int i = 0; i < rs.Length; i++) if (rs[i] && rs[i].enabled != show) rs[i].enabled = show;
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
            root.localScale = (EnemyCatalog.IsPuppet(tag.Kind) ? Vector3.one : ShapeOf(tag.Kind)) * stats.Scale;
            if (EnemyCatalog.IsPuppet(tag.Kind))
            {
                // A boss (or its summons): the game's creature made over (BossLooks), moved by the host's brain.
                _clientTags[tag.ObjectId] = tag;
                if (nob.GetComponent<Creature>() is Creature pc) Bosses.Puppets.Add(pc);
                BossLooks.Dress(root, tag.Kind);
                _dressed.Add(tag.ObjectId);
                AutoTest.Count("dressed_" + tag.Kind);
                return true;
            }

            _clientTags[tag.ObjectId] = tag;
            if (!FoeBodies.Dress(root, tag.Kind, tag.Elite, stats.Gear)) // our own fish's body (Foes.cs); else the game's, dyed and dressed
            {
                // Fit gear to the fish: sit on top of its renderers, sized to its width.
                var rends = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(root.position, Vector3.one);
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float width = Mathf.Max(0.3f, Mathf.Min(b.size.x, b.size.z));
                Vector3 topLocal = root.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
                Tint(root, tag.Kind, tag.Elite); // before the gear goes on, so only the fish is dyed
                if (stats.Gear != null) Wear(root, stats.Gear, topLocal - Vector3.up * (stats.Gear.EndsWith("Shell") ? 0.35f * b.size.y / stats.Scale : 0.05f), width / stats.Scale);
                if (tag.Elite) Wear(root, "Crown", topLocal, width * 0.7f / stats.Scale);
            }
            _dressed.Add(tag.ObjectId);
            _rends[tag.ObjectId] = root.GetComponentsInChildren<Renderer>();
            if (FortressPlugin.Instance == null || !FortressPlugin.Instance.EnemyShadowsOn)
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            AutoTest.Count("dressed_" + tag.Kind);
            return true;
        }

        // ---------- look: a build and a dye per kind (a tinted copy of the fish's palette texture) ----------

        private readonly Dictionary<int, EnemyTag> _clientTags = new Dictionary<int, EnemyTag>();
        private static readonly Dictionary<(Texture, EnemyKind, bool), Texture> Tinted = new Dictionary<(Texture, EnemyKind, bool), Texture>();
        private static Material _blit;

        // Every walker is the game's one walking fish, so each kind is built differently on top of its size (the playtest:
        // "they all look the same, just bigger"): a charger long and low, a tank broad, a thief thin, a healer tall...
        // x across, y up, z nose to tail.
        public static Vector3 ShapeOf(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Charger: return new Vector3(0.85f, 0.8f, 1.35f);
                case EnemyKind.Tank: return new Vector3(1.3f, 1.05f, 1.1f);
                case EnemyKind.Thief: return new Vector3(0.75f, 1.1f, 0.95f);
                case EnemyKind.Shield: return new Vector3(1.25f, 1f, 0.95f);
                case EnemyKind.Digger: return new Vector3(1.15f, 0.75f, 1.15f);
                case EnemyKind.Healer: return new Vector3(0.85f, 1.3f, 0.85f);
                case EnemyKind.Bomber: return new Vector3(1.15f, 1.1f, 1f);
                case EnemyKind.Splitter: return new Vector3(1.15f, 0.9f, 1.15f);
                default: return Vector3.one;
            }
        }

        public static Color DyeOf(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Charger: return new Color(1f, 0.5f, 0.44f);
                case EnemyKind.Bomber: return new Color(0.55f, 0.55f, 0.66f);
                case EnemyKind.Tank: return new Color(0.88f, 0.76f, 0.52f);
                case EnemyKind.Thief: return new Color(0.74f, 0.52f, 1f);
                case EnemyKind.Shield: return new Color(0.55f, 0.74f, 1f);
                case EnemyKind.Digger: return new Color(0.96f, 0.68f, 0.38f);
                case EnemyKind.Healer: return new Color(0.5f, 1f, 0.58f);
                case EnemyKind.Splitter: case EnemyKind.Mini: return new Color(0.78f, 1f, 0.34f);
                default: return Color.white;
            }
        }
        private static readonly Color EliteDye = new Color(1f, 0.85f, 0.42f); // an elite's dye is gilded on top of its kind's

        // Shared tinted materials: 400 fish used to create 400 material instances and, worse, a tinted texture per
        // material. The fish of one kind all look the same, so they share one material per (source material, kind, elite).
        private static readonly Dictionary<(Material, EnemyKind, bool), Material> TintedMats = new Dictionary<(Material, EnemyKind, bool), Material>();

        private static void Tint(Transform root, EnemyKind kind, bool elite)
        {
            Color c = DyeOf(kind) * (elite ? EliteDye : Color.white);
            c.a = 1f;
            if (c == Color.white) return;
            if (!_blit) { var sh = Shader.Find("UI/Default"); if (!sh) return; _blit = new Material(sh); }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var srcs = r.sharedMaterials;
                if (srcs == null || srcs.Length == 0) continue;
                var dsts = new Material[srcs.Length];
                for (int i = 0; i < srcs.Length; i++)
                {
                    var srcMat = srcs[i];
                    if (!srcMat) { dsts[i] = srcMat; continue; }
                    var key = (srcMat, kind, elite);
                    if (!TintedMats.TryGetValue(key, out var tinted))
                    {
                        tinted = new Material(srcMat) { name = srcMat.name + " (" + kind + (elite ? " elite" : "") + ")" };
                        foreach (string prop in new[] { "_Colors", "_BaseMap", "_MainTex" })
                        {
                            if (!tinted.HasProperty(prop) || !(tinted.GetTexture(prop) is Texture src)) continue;
                            if (!Tinted.TryGetValue((src, kind, elite), out var tex))
                            {
                                var rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = src.filterMode, wrapMode = src.wrapMode };
                                _blit.color = c;
                                Graphics.Blit(src, rt, _blit);
                                Tinted[(src, kind, elite)] = tex = rt;
                            }
                            tinted.SetTexture(prop, tex);
                            break;
                        }
                        TintedMats[key] = tinted;
                    }
                    dsts[i] = tinted;
                }
                r.sharedMaterials = dsts;
            }
        }

        internal static void Wear(Transform root, string model, Vector3 localPos, float size)
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
