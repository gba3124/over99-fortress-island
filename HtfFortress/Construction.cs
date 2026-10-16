using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using TMPro;
using UnityEngine;

namespace HtfFortress
{
    // Client -> host: "I'm hammering at this spot" (a few times a second while holding the mouse on a valid preview).
    public struct BuildWork : IBroadcast { public PieceType Type; public Vector3 Pos; public float RotY; }
    // Host -> everyone: a construction site and how far along it is. End: 0 still going, 1 built, 2 abandoned.
    public struct SiteState : IBroadcast { public int Id; public PieceType Type; public Vector3 Pos; public float RotY; public float Progress; public byte Workers; public byte End; }

    // Building takes a moment: holding the mouse on a spot opens a construction site there (the team pays up front),
    // and it fills while someone keeps working on it; every extra teammate on the same site adds 60% speed.
    // Let go for a second and the site is abandoned and refunded. Sites are decided on the host.
    public class Construction : MonoBehaviour
    {
        public static Construction Instance;
        public const float TeamBonus = 0.6f, WorkGap = 0.35f, Abandon = 1.0f, SendEvery = 0.15f;

        public static float BuildTime(PieceType t) => Catalog.IsTurret(t) ? 1.0f : t == PieceType.Spikes ? 0.6f : 0.4f;

        private class Site
        {
            public int Id, Owner, Paid;
            public PieceType Type;
            public Vector3 Pos;
            public float RotY, Progress, LastSent;
            public byte LastWorkers;
            public readonly Dictionary<int, float> Work = new Dictionary<int, float>();
        }

        // host
        private readonly Dictionary<int, Site> _sites = new Dictionary<int, Site>();
        private int _nextId = 1;
        // client
        private class View { public SiteState S; public GameObject Root, Model; public float NextChip, NextHammer; }
        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private readonly List<(Transform t, Vector3 v, float until)> _chips = new List<(Transform, Vector3, float)>();
        private float _lastLocalWork = -10f;
        private int _localSite;
        private NetworkManager _registeredOn;

        static Construction()
        {
            GenericWriter<BuildWork>.SetWrite((w, v) => { w.WriteByte((byte)v.Type); w.WriteVector3(v.Pos); w.WriteSingle(v.RotY); });
            GenericReader<BuildWork>.SetRead(r => new BuildWork { Type = (PieceType)r.ReadByte(), Pos = r.ReadVector3(), RotY = r.ReadSingle() });
            GenericWriter<SiteState>.SetWrite((w, v) =>
            {
                w.WriteInt32(v.Id); w.WriteByte((byte)v.Type); w.WriteVector3(v.Pos); w.WriteSingle(v.RotY); w.WriteSingle(v.Progress); w.WriteByte(v.Workers); w.WriteByte(v.End);
            });
            GenericReader<SiteState>.SetRead(r => new SiteState
            {
                Id = r.ReadInt32(), Type = (PieceType)r.ReadByte(), Pos = r.ReadVector3(), RotY = r.ReadSingle(), Progress = r.ReadSingle(), Workers = r.ReadByte(), End = r.ReadByte(),
            });
        }

        private void Awake() => Instance = this;

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ServerManager.RegisterBroadcast<BuildWork>((conn, w, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) ServerWork(who.ObjectId, w);
                });
                nm.ClientManager.RegisterBroadcast<SiteState>((s, ch) => OnSite(s));
                nm.SceneManager.OnClientLoadedStartScenes += (conn, asServer) =>
                {
                    if (asServer) foreach (var s in _sites.Values) InstanceFinder.ServerManager.Broadcast(conn, StateOf(s, 0));
                };
                nm.ServerManager.OnServerConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) _sites.Clear(); };
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearViews(); };
            }
            if (InstanceFinder.IsServerStarted) ServerTick();
            ClientTick();
        }

        // ---------- client API (BuildSystem calls these while you hold the mouse) ----------

        // Work on whatever is at this spot: an existing site there, or a new one of `type`.
        public void Work(PieceType type, Vector3 pos, float rotY)
        {
            _lastLocalWork = Time.time;
            var near = _views.Values.FirstOrDefault(v => (v.S.Pos - pos).sqrMagnitude < 0.01f);
            _localSite = near != null ? near.S.Id : 0;
            if (near != null) { type = near.S.Type; rotY = near.S.RotY; }
            if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(new BuildWork { Type = type, Pos = pos, RotY = rotY });
        }

        // A teammate's site the crosshair is on (so you can help without picking the same piece).
        public bool SiteUnderAim(Ray ray, float reach, out SiteState site)
        {
            site = default;
            float best = 1.3f;
            foreach (var v in _views.Values)
            {
                Vector3 c = v.S.Pos + Vector3.up * (Catalog.IsTurret(v.S.Type) || v.S.Type == PieceType.Spikes ? 0.6f : 0f);
                float along = Vector3.Dot(c - ray.origin, ray.direction);
                if (along < 0f || along > reach) continue;
                float off = Vector3.Cross(ray.direction, c - ray.origin).magnitude;
                if (off < best) { best = off; site = v.S; }
            }
            return best < 1.3f;
        }

        public bool LocalWorking => Time.time - _lastLocalWork < WorkGap;

        // ---------- host ----------

        // Also used by the auto-test to stand in for a teammate.
        public void ServerWork(int playerId, BuildWork w)
        {
            var site = _sites.Values.FirstOrDefault(s => (s.Pos - w.Pos).sqrMagnitude < 0.01f);
            if (site == null)
            {
                if (!BuildSystem.Instance.ServerCanPlace(w.Type, w.Pos)) return;
                var who = PlayerManager.Players.FirstOrDefault(p => p && p.ObjectId == playerId);
                int cost = Perks.Cost(Catalog.BuildCost(w.Type), who);
                if (!Siege.Instance.ServerTrySpend(cost)) return;
                site = new Site { Id = _nextId++, Owner = playerId, Paid = cost, Type = w.Type, Pos = w.Pos, RotY = w.RotY };
                _sites[site.Id] = site;
                AutoTest.Count("sites_started");
            }
            site.Work[playerId] = Time.time;
        }

        private void ServerTick()
        {
            if (_sites.Count == 0) return;
            float now = Time.time;
            foreach (var s in _sites.Values.ToList())
            {
                if (!Siege.Instance.ServerActive) { End(s, 2, refund: false); continue; }
                int workers = s.Work.Values.Count(t => now - t < WorkGap);
                if (workers == 0)
                {
                    if (now - s.Work.Values.DefaultIfEmpty(0f).Max() > Abandon) { End(s, 2, refund: true); AutoTest.Count("sites_abandoned"); }
                    continue;
                }
                bool fast = s.Work.Any(kv => now - kv.Value < WorkGap && Perks.Legend(kv.Key, Gear.ToolBelt)); // legendary tool belt: twice as fast
                s.Progress += Time.deltaTime * (1f + TeamBonus * (workers - 1)) * (fast ? 2f : 1f) / BuildTime(s.Type);
                if (s.Progress >= 1f)
                {
                    if (BuildSystem.Instance.ServerPlace(s.Type, s.Pos, s.RotY, s.Owner, prepaid: s.Paid)) End(s, 1, refund: false);
                    else End(s, 2, refund: true); // the slot got taken meanwhile
                    continue;
                }
                if (now - s.LastSent > 0.1f || workers != s.LastWorkers)
                {
                    s.LastSent = now;
                    s.LastWorkers = (byte)workers;
                    InstanceFinder.ServerManager.Broadcast(StateOf(s, 0));
                }
            }
        }

        private void End(Site s, byte how, bool refund)
        {
            _sites.Remove(s.Id);
            if (refund) Siege.Instance.ServerAddCoins(s.Paid);
            InstanceFinder.ServerManager.Broadcast(StateOf(s, how));
        }

        private static SiteState StateOf(Site s, byte end) =>
            new SiteState { Id = s.Id, Type = s.Type, Pos = s.Pos, RotY = s.RotY, Progress = Mathf.Clamp01(s.Progress), Workers = s.LastWorkers, End = end };

        public int ServerSiteCount => _sites.Count;

        // ---------- client: what a site looks like ----------

        private void OnSite(SiteState s)
        {
            _views.TryGetValue(s.Id, out var v);
            if (s.End != 0)
            {
                if (v != null)
                {
                    Burst(v, s.End == 1 ? 14 : 6);
                    Destroy(v.Root);
                    _views.Remove(s.Id);
                }
                if (s.End == 2) Fx.Sound("PickupWhoosh", s.Pos, 0.5f, AudioDistance.Short);
                return;
            }
            if (v == null)
            {
                v = new View { Root = new GameObject("FortressSite") };
                DontDestroyOnLoad(v.Root);
                v.Root.transform.position = s.Pos;
                v.Root.transform.rotation = s.Type == PieceType.Ramp ? Quaternion.Euler(-45f, s.RotY, 0f) : Quaternion.Euler(0f, s.RotY, 0f);
                v.Model = BuildSystem.CreatePieceVisual(s.Type);
                v.Model.transform.SetParent(v.Root.transform, false);
                Models.Paint(v.Model, new Color(0.78f, 0.86f, 1f));
                Scaffold(v.Root.transform, s.Type);
                _views[s.Id] = v;
            }
            v.S = s;
        }

        // Grows up from the ground; wood chips fly; a hammer knocks.
        private void ClientTick()
        {
            float now = Time.time;
            foreach (var v in _views.Values)
            {
                float p = v.S.Progress;
                bool centred = v.S.Type == PieceType.Wall;
                v.Model.transform.localScale = new Vector3(1f, Mathf.Max(0.04f, p), 1f);
                v.Model.transform.localPosition = centred ? Vector3.down * (1f - p) * BuildSystem.Cell * 0.5f : Vector3.zero;
                if (now >= v.NextChip) { v.NextChip = now + 0.07f; Chip(v.S.Pos + Random.insideUnitSphere * 1.1f + Vector3.up * 0.3f, Catalog.IsTurret(v.S.Type)); }
                if (now >= v.NextHammer)
                {
                    v.NextHammer = now + 0.26f;
                    if (Catalog.IsTurret(v.S.Type)) Fx.SoundRandom("MetallicItemImpact_V", 1, 2, v.S.Pos, 0.45f, AudioDistance.Short);
                    else Fx.SoundRandom("WoodItemImpact_V", 1, 3, v.S.Pos, 0.45f, AudioDistance.Short);
                }
            }
            for (int i = _chips.Count - 1; i >= 0; i--)
            {
                var (t, vel, until) = _chips[i];
                if (!t || now > until) { if (t) Destroy(t.gameObject); _chips.RemoveAt(i); continue; }
                vel += Physics.gravity * Time.deltaTime;
                t.position += vel * Time.deltaTime;
                t.Rotate(400f * Time.deltaTime, 230f * Time.deltaTime, 0f);
                _chips[i] = (t, vel, until);
            }
            DrawLabels();
        }

        private static Material _woodChip, _metalChip, _scaffold;

        private void Chip(Vector3 at, bool metal)
        {
            if (_chips.Count > 80) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.localScale = Vector3.one * Random.Range(0.06f, 0.12f);
            if (!_woodChip) { _woodChip = Models.Make(new Color(0.72f, 0.55f, 0.33f)); _metalChip = Models.Make(new Color(0.75f, 0.75f, 0.72f)); }
            go.GetComponent<Renderer>().sharedMaterial = metal && Random.value < 0.6f ? _metalChip : _woodChip;
            _chips.Add((go.transform, new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(2f, 4f), Random.Range(-1.5f, 1.5f)), Time.time + 0.6f));
        }

        private void Burst(View v, int n) { for (int i = 0; i < n; i++) Chip(v.S.Pos + Random.insideUnitSphere * 1.2f + Vector3.up * 0.5f, Catalog.IsTurret(v.S.Type)); }

        // A wooden frame around the site's box.
        private static void Scaffold(Transform root, PieceType t)
        {
            Vector3 size = Catalog.Size(t) + new Vector3(0.2f, 0.2f, 0.2f);
            Vector3 c = Catalog.IsTurret(t) || t == PieceType.Spikes ? Vector3.up * size.y * 0.5f : Vector3.zero;
            if (Catalog.IsTurret(t)) size = new Vector3(1.8f, 2.0f, 1.8f);
            if (!_scaffold) _scaffold = Models.Make(new Color(0.55f, 0.4f, 0.24f));
            Vector3 h = size * 0.5f;
            void Bar(Vector3 a, Vector3 b)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.localPosition = c + (a + b) * 0.5f;
                Vector3 d = b - a;
                go.transform.localScale = new Vector3(Mathf.Max(0.06f, Mathf.Abs(d.x)), Mathf.Max(0.06f, Mathf.Abs(d.y)), Mathf.Max(0.06f, Mathf.Abs(d.z)));
                go.GetComponent<Renderer>().sharedMaterial = _scaffold;
            }
            foreach (float y in new[] { -h.y, h.y })
            {
                Bar(new Vector3(-h.x, y, -h.z), new Vector3(h.x, y, -h.z)); Bar(new Vector3(-h.x, y, h.z), new Vector3(h.x, y, h.z));
                Bar(new Vector3(-h.x, y, -h.z), new Vector3(-h.x, y, h.z)); Bar(new Vector3(h.x, y, -h.z), new Vector3(h.x, y, h.z));
            }
            foreach (float x in new[] { -h.x, h.x }) foreach (float z in new[] { -h.z, h.z }) Bar(new Vector3(x, -h.y, z), new Vector3(x, h.y, z));
        }

        // "Building 60%" over every site in view (so teammates see where to help); the ring at the crosshair for your own.
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();

        private void DrawLabels()
        {
            int used = 0;
            var cam = GameInfo.CurCamera;
            if (cam && Hud.Font)
                foreach (var v in _views.Values)
                {
                    if (LocalWorking && v.S.Id == _localSite) continue; // yours has the ring at the crosshair
                    Vector3 wp = v.S.Pos + Vector3.up * (v.S.Type == PieceType.Wall ? 2f : 2.3f);
                    if ((wp - cam.transform.position).sqrMagnitude > 30f * 30f) continue;
                    Vector3 sp = cam.WorldToScreenPoint(wp);
                    if (sp.z < 0f) continue;
                    if (used >= _labels.Count) _labels.Add(Hud.Label(Hud.Root, "", 22f, Vector2.zero, Vector2.zero, new Vector2(260f, 40f), TextAlignmentOptions.Center, bold: true, outline: true, pivot: new Vector2(0.5f, 0.5f)));
                    var l = _labels[used++];
                    l.gameObject.SetActive(true);
                    l.rectTransform.anchorMin = l.rectTransform.anchorMax = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
                    l.rectTransform.anchoredPosition = Vector2.zero;
                    string team = v.S.Workers > 1 ? $"  <color={Hud.Hex(Hud.Good)}>×{1f + TeamBonus * (v.S.Workers - 1):0.#}</color>" : "";
                    l.text = $"{L10n.T("siteLabel", Mathf.RoundToInt(v.S.Progress * 100f))}{team}";
                }
            for (int i = used; i < _labels.Count; i++) if (_labels[i]) _labels[i].gameObject.SetActive(false);

            if (LocalWorking && _views.TryGetValue(_localSite, out var mine)) Hud.Ring(mine.S.Progress, L10n.T("siteLabel", Mathf.RoundToInt(mine.S.Progress * 100f)));
            else if (LocalWorking)
            {
                var v = _views.Values.OrderBy(x => (x.S.Pos - (Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero)).sqrMagnitude).FirstOrDefault();
                if (v != null) { _localSite = v.S.Id; Hud.Ring(v.S.Progress, L10n.T("siteLabel", Mathf.RoundToInt(v.S.Progress * 100f))); }
            }
        }

        private void ClearViews()
        {
            foreach (var v in _views.Values) if (v.Root) Destroy(v.Root);
            _views.Clear();
        }
    }
}
