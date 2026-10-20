using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using TMPro;
using UnityEngine;

namespace HtfFortress
{
    // Host -> everyone, ten times a second: one of the stall's helpers. State 0 idle, 1 off to a fish, 2 off to the stall,
    // 3 knocked out, 255 gone.
    public struct HelperView : IBroadcast { public byte Id; public Vector3 Pos; public float Yaw; public byte Load, Cap, Hp, State; }
    // Client -> host: bought at the stall: 0 one more helper, 1 faster, 2 bigger baskets, 3 the oven.
    public struct HelperBuy : IBroadcast { public byte What; }

    // The fish stall's helpers (the playtest kept asking for someone to pick up and sell the fish): villagers the team hires
    // at the stall, who all round long (between waves too) walk to the fish corpses on the island, fill their baskets and
    // carry them to the stall, where the fishmonger buys them for the team. Anything else lying on the island they pick up too (Owen:
    // "什麼都撿"), into the vault the way a player walking over it would put it (Loot.ServerHelperPick); they're about
    // between rounds as well (hired any time at the stall). Faster legs, bigger baskets and the oven (their fish are sold
    // grilled, worth half again) are bought at the stall too; all of it is kept with the save. Fish that catch one hurt it:
    // knocked out, it drops what it carried and comes back from the stall a while later. It keeps away from fish if it can.
    // All decided on the host; every client draws them from HelperView (a villager in one of the game's outfits, Person.cs, walking with a
    // basket of fish held out in front when carrying).
    public class Helpers : MonoBehaviour
    {
        public static Helpers Instance;
        // Owen: ten times as many helpers, as fast and as much in a basket (it was 3, 5.4 m/s, 6 fish), at prices that grow
        // exponentially. In the game's coins (x10 dollars, Siege.Money): the 2nd helper $1,950, the 10th $15,900, the 30th
        // $3.0M; a level of speed or basket $1,200 at first, $8,800 at Lv10, $65,000 at Lv20, $2.8M for the last.
        public const int MaxHelpers = 30, MaxLevel = 40;
        public static int HirePrice(int have) => Mathf.RoundToInt(150f * Mathf.Pow(1.3f, have));
        public static int UpgradePrice(int level) => Mathf.RoundToInt(120f * Mathf.Pow(1.22f, level)); // a level of speed or basket, from `level`
        public const int OvenPrice = 400;
        public static float Speed(int level) => 3f + 0.6f * level;      // metres a second: 27 at Lv40
        public static int Basket(int level) => 2 + level * 3 / 2;        // fish at once: 62 at Lv40
        public const float OvenBonus = 0.5f, Reach = 1.4f, KoSeconds = 20f, DangerRange = 1.8f, FishAvoid = 4f, BitePerSecond = 0.12f;

        private void Awake() => Instance = this;

        static Helpers()
        {
            GenericWriter<HelperView>.SetWrite((w, v) => { w.WriteByte(v.Id); w.WriteVector3(v.Pos); w.WriteSingle(v.Yaw); w.WriteByte(v.Load); w.WriteByte(v.Cap); w.WriteByte(v.Hp); w.WriteByte(v.State); });
            GenericReader<HelperView>.SetRead(r => new HelperView { Id = r.ReadByte(), Pos = r.ReadVector3(), Yaw = r.ReadSingle(), Load = r.ReadByte(), Cap = r.ReadByte(), Hp = r.ReadByte(), State = r.ReadByte() });
            GenericWriter<HelperBuy>.SetWrite((w, v) => w.WriteByte(v.What));
            GenericReader<HelperBuy>.SetRead(r => new HelperBuy { What = r.ReadByte() });
        }

        // What the next step of each costs (coins), or -1 when there's no next step.
        public static int PriceOf(int what, VaultData v) =>
            what == 0 ? (v.Helpers < MaxHelpers ? HirePrice(v.Helpers) : -1)
            : what == 1 ? (v.HelperSpeed < MaxLevel ? UpgradePrice(v.HelperSpeed) : -1)
            : what == 2 ? (v.HelperLoad < MaxLevel ? UpgradePrice(v.HelperLoad) : -1)
            : what == 3 ? (v.Oven ? -1 : OvenPrice) : -1;

        private NetworkManager _registeredOn;

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ServerManager.RegisterBroadcast(Net.Safe<HelperBuy>((conn, b, ch) => ServerBuy(b.What)));
                nm.ClientManager.RegisterBroadcast<HelperView>((v, ch) => OnView(v));
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearViews(); };
            }
            if (InstanceFinder.IsServerStarted) ServerTick();
            ClientTick();
        }

        // =====================================================================================================
        // host
        // =====================================================================================================

        // Goal: what it's going for, a body (Creature) or a Loot.Lying key.
        private class Hand { public byte Id; public Vector3 Pos, GoalPos; public float Yaw, Hp = 1f, KoUntil; public int Load, Value; public object Goal; public byte State; }
        private readonly List<Hand> _hands = new List<Hand>();
        private float _last, _nextSend;

        // Bought at the stall, round or not, as far as the crew's money goes.
        public bool ServerBuy(byte what)
        {
            var v = Loot.Instance ? Loot.Instance.Vault : null;
            if (v == null) return false;
            int price = PriceOf(what, v);
            if (price < 0 || !Siege.Instance.ServerTrySpend(Siege.Money(price), always: true)) return false;
            if (what == 0) v.Helpers++;
            else if (what == 1) v.HelperSpeed++;
            else if (what == 2) v.HelperLoad++;
            else v.Oven = true;
            Loot.Instance.SaveVault();
            AutoTest.Count("helper_bought_" + what);
            return true;
        }

        // Where one waits by the stall: round it, five to a ring, the rings further out.
        private static Vector3 Home(FortressIsland island, int id) => island.MarketPos + Quaternion.Euler(0f, 70f * id, 0f) * new Vector3(0f, 0f, 2.2f + 0.7f * ((id - 1) / 5));

        // The ground under a helper: the island or the sea's surface, never the fort's buildings (it walks through them),
        // looked for from a step above where it is (not from the sky: a stall's roof is ground too).
        private static float Ground(Vector3 p)
        {
            float y = WaterManager.GetWaterHeight(p);
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, p.y + 1.2f, p.z), Vector3.down, 30f, GameInfo.LevelLayer))
            {
                string n = h.collider.gameObject.name;
                if (n.StartsWith("FortressPiece") || n == "FortressCore") continue;
                y = Mathf.Max(y, h.point.y);
            }
            return y;
        }

        // Ten times a second, not every frame: thirty of them each looking over every fish and every body on the island, and a
        // ray down each, was the host's frame time in a big late wave. The views glide between (ClientTick).
        private float _nextThink;
        private void ServerTick()
        {
            if (Time.time < _nextThink) return;
            _nextThink = Time.time + 0.1f;
            float dt = Mathf.Min(0.3f, Time.time - _last);
            _last = Time.time;
            var island = FortressIsland.Instance;
            var v = Loot.Instance ? Loot.Instance.Vault : null;
            bool on = island && island.Exists && Siege.Instance && v != null && FortressNet.Instance; // round or not
            int want = on ? Mathf.Clamp(v.Helpers, 0, MaxHelpers) : 0;
            while (_hands.Count > want)
            {
                var gone = _hands[_hands.Count - 1];
                _hands.RemoveAt(_hands.Count - 1);
                _sent.Remove(gone.Id);
                InstanceFinder.ServerManager.Broadcast(new HelperView { Id = gone.Id, State = 255 });
            }
            while (_hands.Count < want)
            {
                byte id = (byte)(_hands.Count + 1);
                var p = Home(island, id);
                _hands.Add(new Hand { Id = id, Pos = new Vector3(p.x, Ground(p), p.z) });
            }
            if (_hands.Count == 0 || FortressPlugin.Frozen) return;
            if (Time.time < _heldUntil) { Broadcast(Basket(v.HelperLoad)); return; } // auto-test: standing still for a picture

            float speed = Speed(v.HelperSpeed);
            int cap = Basket(v.HelperLoad);
            var fish = FortressPlugin.Alive.Where(c => c && c._hp.Value > 0 && !Siege.Instance.IsHidden(c)).Select(c => c.transform.position).ToList();
            bool Unsafe(Vector3 at) => fish.Any(f => (f - at).Flat().sqrMagnitude < FishAvoid * FishAvoid);
            // What there is to pick up: the bodies for the baskets, and everything else lying on the island (Loot.ServerLying).
            var open = new Dictionary<object, Vector3>();
            foreach (var c in FortressNet.Instance.ServerCorpses())
                if (island.OnIsland(c.transform.position, FortressIsland.IslandRadius + 4f)) open[c] = c.transform.position;
            foreach (var t in Loot.Instance.ServerLying()) open[t.Key] = t.Pos;
            var claimed = new HashSet<object>(_hands.Where(o => o.Goal != null).Select(o => o.Goal));
            foreach (var h in _hands)
            {
                if (h.State == 3)
                {
                    if (Time.time < h.KoUntil) continue;
                    var home = Home(island, h.Id);
                    h.Pos = new Vector3(home.x, Ground(home), home.z);
                    h.State = 0;
                    h.Hp = 1f;
                }
                // Fish within reach bite; away from them it gets its breath back.
                int biting = fish.Count(f => (f - h.Pos).sqrMagnitude < DangerRange * DangerRange);
                if (biting > 0)
                {
                    h.Hp -= BitePerSecond * biting * dt;
                    if (h.Hp <= 0f) { KnockOut(h); continue; }
                }
                else h.Hp = Mathf.Min(1f, h.Hp + 0.03f * dt);

                // The nearest thing no other helper is after and no fish is near, while the basket has room (full, it's off to
                // the stall).
                if (h.Load >= cap || h.Goal == null || !open.TryGetValue(h.Goal, out h.GoalPos) || Unsafe(h.GoalPos))
                {
                    if (h.Goal != null) claimed.Remove(h.Goal);
                    h.Goal = null;
                    float best = float.MaxValue;
                    if (h.Load < cap)
                        foreach (var kv in open)
                        {
                            float d2 = (kv.Value - h.Pos).sqrMagnitude;
                            if (d2 < best && !claimed.Contains(kv.Key) && !Unsafe(kv.Value)) { best = d2; h.Goal = kv.Key; h.GoalPos = kv.Value; }
                        }
                    if (h.Goal != null) claimed.Add(h.Goal);
                }
                Vector3 goal;
                if (h.Goal != null)
                {
                    h.State = 1;
                    goal = h.GoalPos;
                    if ((goal - h.Pos).Flat().sqrMagnitude < Reach * Reach)
                    {
                        if (h.Goal is Creature body)
                        {
                            int worth = FortressNet.Instance.ServerTakeCorpse(body);
                            if (worth > 0) { h.Load++; h.Value += worth; Fx.Send(FxKind.Scoop, goal, h.Pos + Vector3.up); AutoTest.Count("helper_picked"); }
                        }
                        else if (Loot.Instance.ServerHelperPick(h.Goal, h.Pos + Vector3.up)) AutoTest.Count("helper_looted");
                        open.Remove(h.Goal);
                        claimed.Remove(h.Goal);
                        h.Goal = null;
                    }
                }
                else if (h.Load > 0)
                {
                    h.State = 2;
                    goal = island.CounterPos;
                    if ((goal - h.Pos).Flat().sqrMagnitude < 2.2f * 2.2f) { Sell(h, v, island.CounterPos); continue; }
                }
                else { h.State = 0; goal = Home(island, h.Id); }
                Vector3 to = (goal - h.Pos).Flat();
                float d = to.magnitude;
                if (d > 0.3f)
                {
                    h.Pos += to / d * Mathf.Min(d, speed * dt);
                    h.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                }
                h.Pos.y = Mathf.MoveTowards(h.Pos.y, Ground(h.Pos), 8f * dt);
            }
            Broadcast(cap);
        }

        // Only the ones that moved or changed, and each at least once a second (a screen drops one unseen for 3 s): thirty
        // standing by the stall ten times a second were most of 300 messages a second.
        private readonly Dictionary<byte, (HelperView view, float at)> _sent = new Dictionary<byte, (HelperView, float)>();
        private void Broadcast(int cap)
        {
            if (Time.time < _nextSend) return;
            _nextSend = Time.time + 0.1f;
            foreach (var h in _hands)
            {
                var view = new HelperView { Id = h.Id, Pos = h.Pos, Yaw = h.Yaw, Load = (byte)h.Load, Cap = (byte)cap, Hp = (byte)Mathf.RoundToInt(Mathf.Clamp01(h.Hp) * 255f), State = h.State };
                if (_sent.TryGetValue(h.Id, out var was) && Time.time - was.at < 1f && Same(was.view, view)) continue;
                _sent[h.Id] = (view, Time.time);
                InstanceFinder.ServerManager.Broadcast(view);
            }
        }
        private static bool Same(HelperView a, HelperView b) =>
            (a.Pos - b.Pos).sqrMagnitude < 0.0025f && Mathf.Abs(Mathf.DeltaAngle(a.Yaw, b.Yaw)) < 1f && a.Load == b.Load && a.Cap == b.Cap && a.Hp == b.Hp && a.State == b.State;

        private void Sell(Hand h, VaultData v, Vector3 counter)
        {
            int value = Mathf.RoundToInt(h.Value * (1f + (v.Oven ? OvenBonus : 0f)));
            if (value > 0)
            {
                Siege.Instance.ServerPay(value);
                Fx.Send(FxKind.Sell, counter, counter, 0, value);
                AutoTest.Count("helper_sold");
                AutoTest.Count("helper_money", value);
            }
            h.Load = 0;
            h.Value = 0;
        }

        private void KnockOut(Hand h)
        {
            h.State = 3;
            h.KoUntil = Time.time + KoSeconds;
            h.Load = 0;
            h.Value = 0;
            h.Goal = null;
            Fx.Send(FxKind.Dust, h.Pos, h.Pos);
            AutoTest.Count("helper_down");
        }

        // Auto-test: where they are and what they carry.
        public string DebugState() => string.Join(", ", _hands.Select(h => $"#{h.Id} {(new[] { "idle", "fetching", "selling", "down" })[Mathf.Min(3, (int)h.State)]} {h.Load} fish ({h.Value}) hp {h.Hp:0.00}"));
        public int DebugCount => _hands.Count;
        // Auto-test: what helper 1's body is made of on this screen.
        public string DebugBody() => _views.TryGetValue(1, out var v) && v.Person
            ? string.Join("; ", v.Person.GetComponentsInChildren<Renderer>(true).Select(r => $"{r.name} {r.bounds.size}")) + $" | root {v.Root.position}"
            : "no view";
        public Vector3 DebugPos(int i) => _hands[i].Pos;
        // Auto-test: helper i stands at `at` facing `yaw` with `load` fish (or knocked out) for `seconds`.
        private float _heldUntil;
        public void DebugStage(int i, Vector3 at, float yaw, int load, bool down, float seconds)
        {
            var h = _hands[i];
            h.Pos = at; h.Yaw = yaw; h.Load = load; h.State = (byte)(down ? 3 : load > 0 ? 2 : 0); h.Hp = down ? 0f : 0.6f;
            if (down) h.KoUntil = Time.time + seconds;
            _heldUntil = Time.time + seconds;
        }

        // =====================================================================================================
        // every client: a villager walking about, a net bag of fish when it carries some, its name, load and health
        // =====================================================================================================

        private class View { public Transform Root, Bag; public Person Person; public Vector3 Pos, Shown; public float Yaw, Seen, Walk; public byte Load, Cap, Hp, State; }
        private readonly Dictionary<byte, View> _views = new Dictionary<byte, View>();
        private readonly List<(RectTransform root, TextMeshProUGUI label, Hud.Bar bar)> _plates = new List<(RectTransform, TextMeshProUGUI, Hud.Bar)>();

        private void OnView(HelperView s)
        {
            if (s.State == 255) { Drop(s.Id); return; }
            if (!_views.TryGetValue(s.Id, out var v))
            {
                var root = new GameObject("FortressHelper" + s.Id).transform;
                root.position = s.Pos;
                var person = Person.Make(root, "Helper" + s.Id);
                person.transform.localScale = Vector3.one * 0.82f; // young ones: smaller than the keepers
                var bag = new GameObject("Basket").transform; // held out in front in both hands
                bag.SetParent(person.transform, false);
                bag.localPosition = new Vector3(0f, 0.86f, 0.5f);
                Models.Attach("HelperBasket", bag, Vector3.one * 0.42f);
                v = _views[s.Id] = new View { Root = root, Person = person, Bag = bag, Shown = s.Pos };
            }
            if (v.State != 3 && s.State == 3) { ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("helperDown", (int)KoSeconds)); Fx.SoundRandom("ItemImpactHeavy_V", 1, 3, s.Pos, 0.8f); }
            v.Pos = s.Pos; v.Yaw = s.Yaw; v.Load = s.Load; v.Cap = s.Cap; v.Hp = s.Hp; v.State = s.State;
            v.Seen = Time.time;
        }

        private void Drop(byte id)
        {
            if (_views.TryGetValue(id, out var v) && v.Root) Destroy(v.Root.gameObject);
            _views.Remove(id);
        }

        private void ClearViews()
        {
            foreach (var id in _views.Keys.ToList()) Drop(id);
        }

        private void ClientTick()
        {
            var cam = GameInfo.CurCamera;
            foreach (var id in _views.Keys.ToList())
            {
                var v = _views[id];
                if (!v.Root || Time.time - v.Seen > 3f) { Drop(id); continue; }
                Vector3 before = v.Shown;
                v.Shown = Vector3.Lerp(v.Shown, v.Pos, 1f - Mathf.Exp(-12f * Time.deltaTime));
                float moving = (v.Shown - before).Flat().magnitude / Mathf.Max(1e-4f, Time.deltaTime);
                v.Walk += Time.deltaTime * Mathf.Clamp(moving, 0f, 6f) * 3.2f;
                bool down = v.State == 3, carrying = v.Load > 0 && !down;
                // Walking (legs and arms swing, a little waddle), the basket held out in front; flat on its back when knocked out.
                v.Root.position = v.Shown;
                var face = Quaternion.Euler(0f, v.Yaw, 0f);
                v.Root.rotation = Quaternion.Slerp(v.Root.rotation, down ? face * Quaternion.Euler(-80f, 0f, 0f) : face * Quaternion.Euler(0f, 0f, moving > 0.2f ? Mathf.Sin(v.Walk) * 4f : 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
                // Posed every frame up close; further off, ten times a second is plenty (up to thirty bodies of bones).
                bool near = !cam || (v.Shown - cam.transform.position).sqrMagnitude < 45f * 45f;
                if (v.Person && (near || (Time.frameCount + id) % 6 == 0)) v.Person.Pose(v.Walk, down ? 0f : Mathf.Clamp01(moving / 2.5f), carrying, 0f, 0f, down ? -20f : 0f);
                if (v.Bag)
                {
                    if (v.Bag.gameObject.activeSelf != carrying) v.Bag.gameObject.SetActive(carrying);
                    if (carrying) v.Bag.localScale = Vector3.one * Mathf.Lerp(0.75f, 1.15f, v.Load / (float)Mathf.Max(1, v.Cap));
                }
            }
            DrawPlates();
        }

        // Over each: "Helper 2/4" and, when hurt, its health.
        private void DrawPlates()
        {
            var cam = GameInfo.CurCamera;
            int used = 0;
            if (cam && Hud.Font && !Reveal.Playing)
                foreach (var v in _views.Values)
                {
                    if (!v.Root) continue;
                    float dist = Vector3.Distance(cam.transform.position, v.Shown);
                    if (dist > 30f) continue;
                    Vector3 sp = cam.WorldToScreenPoint(v.Shown + Vector3.up * 2.1f);
                    if (sp.z < 0f) continue;
                    if (used >= _plates.Count)
                    {
                        var rt = Hud.Node(Hud.Root, "HelperPlate", Vector2.zero, Vector2.zero, new Vector2(160f, 40f), new Vector2(0.5f, 0f));
                        var label = Hud.Label(rt, "", 17f, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(260f, 26f), TextAlignmentOptions.Bottom, bold: true, outline: true);
                        var bar = Hud.Bar.Make(rt, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(64f, 8f), Hud.Good);
                        _plates.Add((rt, label, bar));
                    }
                    var (root, text, hp) = _plates[used++];
                    root.gameObject.SetActive(true);
                    root.anchorMin = root.anchorMax = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
                    root.anchoredPosition = Vector2.zero;
                    root.localScale = Vector3.one * Mathf.Clamp(14f / Mathf.Max(1f, dist), 0.65f, 1.1f);
                    text.text = v.State == 3 ? L10n.T("helperKo") : L10n.T("helperPlate", v.Load, v.Cap);
                    text.color = v.State == 3 ? Hud.Bad : new Color(0.75f, 1f, 0.8f);
                    bool hurt = v.Hp < 250 && v.State != 3;
                    hp.Back.gameObject.SetActive(hurt);
                    if (hurt) hp.Set(v.Hp / 255f, Hud.HealthColor(v.Hp / 255f));
                }
            for (int i = used; i < _plates.Count; i++) if (_plates[i].root) _plates[i].root.gameObject.SetActive(false);
        }
    }
}
