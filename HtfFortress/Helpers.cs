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
    // 3 knocked out, 255 gone. Kind 0 villager, 1 seagull.
    public struct HelperView { public byte Id; public Vector3 Pos; public float Yaw; public byte Load, Cap, Hp, State, Kind; }
    // The same, all the ones that changed this tick in ONE message: thirty helpers used to be up to 300 little broadcasts a
    // second, each with its own header and its own trip through FishNet.
    public struct HelperViews : IBroadcast { public HelperView[] Views; }
    // Client -> host, at the stall: 0 one more helper, 1 faster, 2 bigger baskets, 3 the oven, 4 dismiss the newest one.
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
        public const int MaxHelpers = 10, MaxLevel = 40;
        // The first 5 helpers are villagers; the last 5 are seagulls (Owen): they fly (so fish can't catch them),
        // are quicker, pick things up from farther, and search the whole island instead of the 60 m around them.
        public const int HumanHelpers = 5;
        public const float GullReach = 3.6f, GullSpeed = 1.45f, GullFly = 1.9f, HumanSearch = 60f, GullSearch = 200f, GullSeaReach = 200f;
        public static int HirePrice(int have) => Mathf.RoundToInt(150f * Mathf.Pow(1.3f, have));
        public static int UpgradePrice(int level) => Mathf.RoundToInt(120f * Mathf.Pow(1.22f, level)); // a level of speed or basket, from `level`
        public const int OvenPrice = 400;
        public static float Speed(int level) => 3f + 0.6f * level;      // metres a second: 27 at Lv40
        public static int Basket(int level) => 2 + level * 3 / 2;        // fish at once: 62 at Lv40
        public const float OvenBonus = 0.5f, Reach = 1.4f, KoSeconds = 20f, DangerRange = 1.8f, FishAvoid = 4f, BitePerSecond = 0.12f;

        private void Awake() => Instance = this;

        static Helpers()
        {
            GenericWriter<HelperViews>.SetWrite((w, v) =>
            {
                int n = v.Views?.Length ?? 0;
                w.WriteInt32(n);
                for (int i = 0; i < n; i++)
                {
                    var h = v.Views[i];
                    w.WriteByte(h.Id); w.WriteVector3(h.Pos); w.WriteSingle(h.Yaw);
                    w.WriteByte(h.Load); w.WriteByte(h.Cap); w.WriteByte(h.Hp); w.WriteByte(h.State); w.WriteByte(h.Kind);
                }
            });
            GenericReader<HelperViews>.SetRead(r =>
            {
                int n = r.ReadInt32();
                var arr = new HelperView[n];
                for (int i = 0; i < n; i++)
                    arr[i] = new HelperView { Id = r.ReadByte(), Pos = r.ReadVector3(), Yaw = r.ReadSingle(), Load = r.ReadByte(), Cap = r.ReadByte(), Hp = r.ReadByte(), State = r.ReadByte(), Kind = r.ReadByte() };
                return new HelperViews { Views = arr };
            });
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
                nm.ClientManager.RegisterBroadcast<HelperViews>((b, ch) => { if (b.Views != null) for (int i = 0; i < b.Views.Length; i++) OnView(b.Views[i]); });
                nm.ClientManager.OnClientConnectionState += a => { if (a.ConnectionState == LocalConnectionState.Stopped) ClearViews(); };
            }
            if (InstanceFinder.IsServerStarted) { ServerTick(); ServerGlide(); ServerReach(); }
            ClientTick();
        }

        // =====================================================================================================
        // host
        // =====================================================================================================

        // Goal: what it's going for, a body (Creature) or a Loot.Lying key.
        private class Hand { public byte Id; public Vector3 Pos, GoalPos; public float Yaw, TargetY, Hp = 1f, KoUntil; public int Load, Value; public object Goal; public byte State; public readonly List<object> Plan = new List<object>();
            public bool Gull => Id > HumanHelpers; }
        private readonly List<Hand> _hands = new List<Hand>();
        private float _last, _nextSend;
        private int _lastCap = 2;

        // Bought at the stall, round or not, as far as the crew's money goes.
        public bool ServerBuy(byte what)
        {
            var v = Loot.Instance ? Loot.Instance.Vault : null;
            if (v == null) return false;
            if (what == 4) // dismiss the newest helper: free, no refund (seagulls go first: they are the ones hired last)
            {
                if (v.Helpers <= 0) return false;
                v.Helpers--;
                Loot.Instance.SaveVault();
                AutoTest.Count("helper_fired");
                return true;
            }
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
        // looked for from a step above where it is (not from the sky: a stall's roof is ground too). NonAlloc: this is called
        // for every helper ten times a second, and RaycastAll's array each time was a steady stream of garbage.
        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];
        private static float Ground(Vector3 p)
        {
            float y = WaterManager.GetWaterHeight(p);
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 1.2f, p.z), Vector3.down, GroundHits, 30f, GameInfo.LevelLayer, QueryTriggerInteraction.UseGlobal);
            for (int i = 0; i < n; i++)
            {
                string name = GroundHits[i].collider.gameObject.name;
                if (name.StartsWith("FortressPiece") || name == "FortressCore") continue;
                y = Mathf.Max(y, GroundHits[i].point.y);
            }
            return y;
        }

        // Reused every think (ten times a second): the fish positions, what's lying about, and who is already after what.
        private static readonly List<Vector3> _fishPos = new List<Vector3>();
        private readonly Dictionary<object, (Vector3 pos, bool last, bool sea, bool unsafePos)> _open = new Dictionary<object, (Vector3 pos, bool last, bool sea, bool unsafePos)>(); // sea: a gull-only rescue outside the island
        private readonly List<object> _jobs = new List<object>(); // shared FIFO: oldest unclaimed thing first
        private readonly HashSet<object> _claimed = new HashSet<object>();
        private static bool Danger(Vector3 at)
        {
            for (int i = 0; i < _fishPos.Count; i++) if ((_fishPos[i] - at).Flat().sqrMagnitude < FishAvoid * FishAvoid) return true;
            return false;
        }

        // Ten times a second, not every frame: thirty of them each looking over every fish and every body on the island, and a
        // ray down each, was the host's frame time in a big late wave. The views glide between (ClientTick).
        private float _nextThink;
        private void ServerTick()
        {
            if (Time.time < _nextThink) return;
            // Idle helpers do not need to scan the island five times a second. Busy ones (carrying / walking to a body /
            // walking home) keep the faster 4 Hz; everyone standing at the stall drops to 2 Hz.
            float gap = 0.5f;
            for (int i = 0; i < _hands.Count; i++)
            {
                var h = _hands[i];
                if (h.Load > 0 || h.Goal != null || h.State != 0) { gap = 0.25f; break; }
            }
            _nextThink = Time.time + gap;
            float dt = Mathf.Min(0.3f, Time.time - _last);
            _last = Time.time;
            var island = FortressIsland.Instance;
            var v = Loot.Instance ? Loot.Instance.Vault : null;
            bool on = island && island.Exists && Siege.Instance && v != null && FortressNet.Instance; // round or not
            int want = on ? Mathf.Clamp(v.Helpers, 0, MaxHelpers) : 0;
            _goneViews.Clear();
            while (_hands.Count > want)
            {
                var gone = _hands[_hands.Count - 1];
                _hands.RemoveAt(_hands.Count - 1);
                _sent.Remove(gone.Id);
                _goneViews.Add(new HelperView { Id = gone.Id, State = 255 });
            }
            if (_goneViews.Count > 0)
                InstanceFinder.ServerManager.Broadcast(new HelperViews { Views = _goneViews.ToArray() });
            while (_hands.Count < want)
            {
                byte id = (byte)(_hands.Count + 1);
                var p = Home(island, id);
                float gy = Ground(p) + (id > HumanHelpers ? GullFly : 0f);
                _hands.Add(new Hand { Id = id, Pos = new Vector3(p.x, gy, p.z), TargetY = gy });
            }
            if (_hands.Count == 0 || FortressPlugin.Frozen) return;
            if (Time.time < _heldUntil) { Broadcast(Basket(v.HelperLoad)); return; } // auto-test: standing still for a picture

            float speed = Speed(v.HelperSpeed);
            int cap = Basket(v.HelperLoad); _lastCap = cap;
            // Reused every think: the lists below used to be rebuilt (and sorted) ten times a second for every helper.
            _fishPos.Clear();
            var alive = FortressPlugin.Alive;
            for (int i = 0; i < alive.Count; i++)
            {
                var c = alive[i];
                if (c && c._hp.Value > 0 && !Siege.Instance.IsHidden(c)) _fishPos.Add(c.transform.position);
            }
            // What there is to pick up: the bodies for the baskets, and everything else lying on the island (Loot.ServerLying).
            _open.Clear();
            foreach (var c in FortressNet.Instance.ServerCorpses())
            {
                Vector3 cp = c.transform.position;
                bool onIsland = island.OnIsland(cp, FortressIsland.IslandRadius + 4f);
                if (!onIsland && (cp - island.Center).sqrMagnitude > GullSeaReach * GullSeaReach) continue; // too far out even for a gull
                _open[c] = (cp, false, !onIsland, Danger(cp)); // bodies out at sea are for the seagulls, not the villagers
            }
            foreach (var t in Loot.Instance.ServerLying())
            {
                bool onIsland = island.OnIsland(t.Pos, FortressIsland.IslandRadius + 4f);
                if (!onIsland && (t.Pos - island.Center).sqrMagnitude > GullSeaReach * GullSeaReach) continue;
                _open[t.Key] = (t.Pos, t.Last, !onIsland, Danger(t.Pos));
            }
            // One shared work queue for all helpers: the first eligible job is the oldest thing still waiting (FIFO),
            // not a fresh nearest-item scan per helper. The queue is rebuilt from what is actually still there.
            _jobs.Clear();
            foreach (var k in _open.Keys) _jobs.Add(k);
            _claimed.Clear();
            for (int i = 0; i < _hands.Count; i++)
            {
                var h = _hands[i];
                if (h.Goal != null) _claimed.Add(h.Goal);
                for (int j = 0; j < h.Plan.Count; j++) _claimed.Add(h.Plan[j]);
            }
            foreach (var h in _hands)
            {
                if (h.State == 3)
                {
                    if (Time.time < h.KoUntil) continue;
                    var home = Home(island, h.Id);
                    float hy = Ground(home);
                    h.Pos = new Vector3(home.x, hy, home.z); h.TargetY = hy;
                    h.State = 0;
                    h.Hp = 1f;
                }
                // Fish within reach bite; away from them it gets its breath back. Seagulls fly over them.
                int biting = 0;
                if (!h.Gull)
                    for (int i = 0; i < _fishPos.Count; i++)
                        if ((_fishPos[i] - h.Pos).sqrMagnitude < DangerRange * DangerRange) biting++;
                if (biting > 0)
                {
                    h.Hp -= BitePerSecond * biting * dt;
                    if (h.Hp <= 0f) { KnockOut(h); continue; }
                }
                else h.Hp = Mathf.Min(1f, h.Hp + 0.03f * dt);

                // One helper owns a KNN batch around the nearest drop, not the oldest item on the island: it fills its
                // basket with what is next to each other, and other helpers skip that batch. When the batch is empty it
                // re-plans. (Owen: "附近的可以拿的拿一拿"; FIFO had helpers marching across the whole island.)
                bool goalOk = h.Goal != null && _open.TryGetValue(h.Goal, out var cur) && (h.Gull || (!cur.sea && !cur.unsafePos));
                if (h.Load >= cap)
                {
                    ReleasePlan(h);
                    h.GoalPos = island.CounterPos;
                }
                else
                {
                    if (!goalOk && h.Goal != null)
                    {
                        _claimed.Remove(h.Goal); h.Plan.Remove(h.Goal); h.Goal = null;
                    }
                    if (h.Goal == null) NextInPlan(h);
                    if (h.Goal == null) PlanNearest(h, cap);
                }
                if (h.Goal != null) h.State = 1;
                else if (h.Load > 0) { h.State = 2; h.GoalPos = island.CounterPos; }
                else h.State = 0;
                // Position is advanced every frame by ServerGlide: walking in 0.25-0.5 s steps was the visible stutter.
                h.TargetY = Ground(h.Pos) + (h.Gull ? GullFly : 0f);
            }
            Broadcast(cap);
        }


        // A helper claims up to a basketful of nearby jobs: the nearest seed, then everything within a short radius of
        // that seed, ordered by walking distance from the helper. This is the KNN batch the island was missing.
        public const float ClusterRadius = 6f;
        private void ReleasePlan(Hand h)
        {
            if (h.Goal != null) _claimed.Remove(h.Goal);
            foreach (var k in h.Plan) _claimed.Remove(k);
            h.Plan.Clear();
            h.Goal = null;
        }
        private bool Eligible(Hand h, object key, Vector3 pos, bool lastOne, bool sea, bool unsafePos, float search)
        {
            if (_claimed.Contains(key)) return false;
            if (lastOne) return false; // trophies are a second pass only
            if (sea && !h.Gull) return false;
            if (!h.Gull && unsafePos) return false;
            return (pos - h.Pos).sqrMagnitude <= search * search;
        }
        private void NextInPlan(Hand h)
        {
            if (h.Load >= _lastCap) { ReleasePlan(h); return; }
            object bestKey = null; float best = float.MaxValue;
            for (int i = h.Plan.Count - 1; i >= 0; i--)
            {
                var key = h.Plan[i];
                if (!_open.TryGetValue(key, out var job)) { _claimed.Remove(key); h.Plan.RemoveAt(i); continue; }
                float d2 = (job.pos - h.Pos).sqrMagnitude;
                if (d2 < best) { best = d2; bestKey = key; h.GoalPos = job.pos; }
            }
            h.Goal = bestKey;
        }
        private void PlanNearest(Hand h, int cap)
        {
            if (h.Load >= cap) return;
            float search = h.Gull ? GullSearch : HumanSearch;
            object seed = null; float seedD = float.MaxValue;
            // Ordinary loot first; a trophy only if there is nothing else reachable.
            for (int pass = 0; pass < 2 && seed == null; pass++)
                for (int i = 0; i < _jobs.Count; i++)
                {
                    var key = _jobs[i];
                    if (!_open.TryGetValue(key, out var job)) continue;
                    if (pass == 0 && job.last) continue;
                    if (pass == 1 && !job.last) continue;
                    if (_claimed.Contains(key) || (!h.Gull && (job.sea || job.unsafePos))) continue;
                    if ((job.pos - h.Pos).sqrMagnitude > search * search) continue;
                    float d2 = (job.pos - h.Pos).sqrMagnitude;
                    if (d2 < seedD) { seedD = d2; seed = key; }
                }
            if (seed == null || !_open.TryGetValue(seed, out var seedJob)) return;
            int room = Mathf.Max(1, cap - h.Load);
            var near = new List<(object key, Vector3 pos, float d)>();
            for (int i = 0; i < _jobs.Count && near.Count < 64; i++)
            {
                var key = _jobs[i];
                if (!_open.TryGetValue(key, out var job)) continue;
                if ((job.pos - seedJob.pos).sqrMagnitude > ClusterRadius * ClusterRadius) continue;
                if (_claimed.Contains(key) || (!h.Gull && (job.sea || job.unsafePos))) continue;
                if ((job.pos - h.Pos).sqrMagnitude > search * search) continue;
                near.Add((key, job.pos, (job.pos - h.Pos).sqrMagnitude));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < near.Count && h.Plan.Count < room; i++)
            {
                var key = near[i].key;
                if (_claimed.Contains(key)) continue;
                h.Plan.Add(key); _claimed.Add(key);
            }
            NextInPlan(h);
        }

        // Host movement, every frame. Decisions stay at 2-4 Hz; only the cheap part (walk toward the cached goal) runs at
        // frame rate, so both the host's own helpers and the 10 Hz HelperViews stay smooth.
        private void ServerGlide()
        {
            if (_hands.Count == 0 || FortressPlugin.Frozen || Time.time < _heldUntil) return;
            var island = FortressIsland.Instance;
            var v = Loot.Instance ? Loot.Instance.Vault : null;
            if (!island || !island.Exists || v == null) return;
            float speed = Speed(v.HelperSpeed);
            float dt = Mathf.Min(0.1f, Time.deltaTime);
            bool any = false;
            foreach (var h in _hands)
            {
                if (h.State == 3) continue;
                Vector3 goal;
                if (h.Goal != null) { h.State = 1; goal = h.GoalPos; }
                else if (h.Load > 0) { h.State = 2; goal = island.CounterPos; }
                else { h.State = 0; goal = Home(island, h.Id); }
                Vector3 to = (goal - h.Pos).Flat();
                float d = to.magnitude;
                if (d > 0.3f)
                {
                    h.Pos += to / d * Mathf.Min(d, speed * (h.Gull ? GullSpeed : 1f) * dt);
                    h.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    any = true;
                }
                float wantY = h.TargetY;
                if (Mathf.Abs(h.Pos.y - wantY) > 0.001f)
                {
                    h.Pos.y = Mathf.MoveTowards(h.Pos.y, wantY, (h.Gull ? 6f : 8f) * dt);
                    any = true;
                }
            }
            if (any) Broadcast(_lastCap);
        }

        // Only the ones that moved or changed, and each at least once a second (a screen drops one unseen for 3 s): thirty
        // standing by the stall ten times a second were most of 300 messages a second.
        private readonly Dictionary<byte, (HelperView view, float at)> _sent = new Dictionary<byte, (HelperView, float)>();
        private readonly List<HelperView> _batch = new List<HelperView>(32);
        private readonly List<HelperView> _goneViews = new List<HelperView>(4); // dropped helpers: one message too
        private void Broadcast(int cap)
        {
            if (Time.time < _nextSend) return;
            _nextSend = Time.time + 0.1f;
            _batch.Clear();
            foreach (var h in _hands)
            {
                var view = new HelperView { Id = h.Id, Pos = h.Pos, Yaw = h.Yaw, Load = (byte)h.Load, Cap = (byte)cap, Hp = (byte)Mathf.RoundToInt(Mathf.Clamp01(h.Hp) * 255f), State = h.State, Kind = (byte)(h.Gull ? 1 : 0) };
                if (_sent.TryGetValue(h.Id, out var was) && Time.time - was.at < 1f && Same(was.view, view)) continue;
                _sent[h.Id] = (view, Time.time);
                _batch.Add(view);
            }
            if (_batch.Count > 0) InstanceFinder.ServerManager.Broadcast(new HelperViews { Views = _batch.ToArray() });
        }
        private static bool Same(HelperView a, HelperView b) =>
            (a.Pos - b.Pos).sqrMagnitude < 0.0025f && Mathf.Abs(Mathf.DeltaAngle(a.Yaw, b.Yaw)) < 1f && a.Load == b.Load && a.Cap == b.Cap && a.Hp == b.Hp && a.State == b.State;

        // Reaching/picking/selling runs every frame too: the old 2-4 Hz decision tick was fine for *deciding* what to
        // fetch, but it also limited collection to two or four items a second. A helper now takes what it touches at once.
        private void ServerReach()
        {
            if (_hands.Count == 0 || FortressPlugin.Frozen || Time.time < _heldUntil) return;
            var island = FortressIsland.Instance;
            var v = Loot.Instance ? Loot.Instance.Vault : null;
            if (!island || !island.Exists || v == null) return;
            int cap = _lastCap;
            foreach (var h in _hands)
            {
                if (h.State == 3) continue;
                if (h.Goal != null && _open.TryGetValue(h.Goal, out var job))
                {
                    float reach = h.Gull ? GullReach : Reach;
                    if ((job.pos - h.Pos).Flat().sqrMagnitude < reach * reach)
                    {
                        object taken = h.Goal;
                        if (taken is Creature body)
                        {
                            int worth = FortressNet.Instance.ServerTakeCorpse(body);
                            if (worth > 0) { h.Load++; h.Value += worth; Fx.Send(FxKind.Scoop, job.pos, h.Pos + Vector3.up); AutoTest.Count("helper_picked"); }
                        }
                        else if (Loot.Instance.ServerHelperPick(taken, h.Pos + Vector3.up)) AutoTest.Count("helper_looted");
                        _open.Remove(taken); _claimed.Remove(taken); h.Plan.Remove(taken); h.Goal = null;
                        if (h.Load < cap) NextInPlan(h);
                        if (h.Load >= cap || h.Goal == null) ReleasePlan(h);
                    }
                }
                else if (h.Goal == null && h.Load > 0 && (island.CounterPos - h.Pos).Flat().sqrMagnitude < 2.2f * 2.2f)
                    Sell(h, v, island.CounterPos);
            }
        }

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
            ReleasePlan(h);
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

        private class View { public Transform Root, Bag; public Person Person; public Vector3 Pos, Shown; public float Yaw, Seen, Walk; public byte Load, Cap, Hp, State;
            public bool Gull; }
        private readonly Dictionary<byte, View> _views = new Dictionary<byte, View>();
        public int ViewCount => _views.Count;
        private readonly List<(RectTransform root, TextMeshProUGUI label, Hud.Bar bar)> _plates = new List<(RectTransform, TextMeshProUGUI, Hud.Bar)>();

        private void OnView(HelperView s)
        {
            if (s.State == 255) { Drop(s.Id); return; }
            if (!_views.TryGetValue(s.Id, out var v))
            {
                var root = new GameObject("FortressHelper" + s.Id).transform;
                root.position = s.Pos;
                if (s.Kind == 1)
                {
                    // A seagull (tools/make_seagull.py): the same villager slot past HumanHelpers, but it flies.
                    var gull = Models.Attach("Seagull", root, new Vector3(0.7f, 0.2f, 0.7f), new Vector3(0f, 0.3f, 0f), shadows: false, share: false);
                    gull.transform.localScale = Vector3.one * 0.95f;
                    v = _views[s.Id] = new View { Root = root, Gull = true, Shown = s.Pos };
                }
                else
                {
                    var person = Person.Make(root, "Helper" + s.Id);
                    person.transform.localScale = Vector3.one * 0.82f; // young ones: smaller than the keepers
                    var bag = new GameObject("Basket").transform; // held out in front in both hands
                    bag.SetParent(person.transform, false);
                    bag.localPosition = new Vector3(0f, 0.86f, 0.5f);
                    Models.Attach("HelperBasket", bag, Vector3.one * 0.42f, shadows: false, share: false);
                    v = _views[s.Id] = new View { Root = root, Person = person, Bag = bag, Shown = s.Pos };
                }
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

        private readonly List<byte> _gone = new List<byte>(); // reused: ClientTick runs every frame
        private void ClientTick()
        {
            var cam = GameInfo.CurCamera;
            _gone.Clear();
            foreach (var kv in _views)
            {
                byte id = kv.Key;
                var v = kv.Value;
                if (!v.Root || Time.time - v.Seen > 3f) { _gone.Add(id); continue; }
                // Far helpers aren't drawn (they are still simulated on the host): a dock full of thirty models is draw calls.
                if (cam)
                {
                    bool near = (v.Shown - cam.transform.position).sqrMagnitude < 120f * 120f;
                    if (v.Root.gameObject.activeSelf != near) v.Root.gameObject.SetActive(near);
                }
                Vector3 before = v.Shown;
                v.Shown = Vector3.Lerp(v.Shown, v.Pos, 1f - Mathf.Exp(-12f * Time.deltaTime));
                float moving = (v.Shown - before).Flat().magnitude / Mathf.Max(1e-4f, Time.deltaTime);
                v.Walk += Time.deltaTime * Mathf.Clamp(moving, 0f, 6f) * 3.2f;
                bool down = v.State == 3, carrying = v.Load > 0 && !down;
                var face = Quaternion.Euler(0f, v.Yaw, 0f);
                if (v.Gull)
                {
                    // Seagulls fly: a little bob and bank, no walking bones.
                    v.Root.position = v.Shown + Vector3.up * (0.10f * Mathf.Sin(Time.time * 3.5f + id));
                    v.Root.rotation = Quaternion.Slerp(v.Root.rotation,
                        face * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2.6f + id) * 8f), 1f - Mathf.Exp(-8f * Time.deltaTime));
                }
                else
                {
                    // Walking (legs and arms swing, a little waddle), the basket held out in front; flat on its back when knocked out.
                    v.Root.position = v.Shown;
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
            }
            for (int i = 0; i < _gone.Count; i++) Drop(_gone[i]);
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
                    Vector3 sp = cam.WorldToScreenPoint(v.Shown + Vector3.up * (v.Gull ? 1.2f : 2.1f));
                    if (sp.z < 0f || sp.x < -40f || sp.x > Screen.width + 40f || sp.y < -40f || sp.y > Screen.height + 40f) continue; // no half-clipped plates at the screen edge
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
                    string want = v.State == 3 ? L10n.T("helperKo") : L10n.T(v.Gull ? "helperGullPlate" : "helperPlate", v.Load, v.Cap);
                    if (text.text != want) text.text = want;
                    Color wantColor = v.State == 3 ? Hud.Bad : new Color(0.75f, 1f, 0.8f);
                    if (text.color != wantColor) text.color = wantColor;
                    bool hurt = v.Hp < 250 && v.State != 3;
                    hp.Back.gameObject.SetActive(hurt);
                    if (hurt) hp.Set(v.Hp / 255f, Hud.HealthColor(v.Hp / 255f));
                }
            for (int i = used; i < _plates.Count; i++) if (_plates[i].root) _plates[i].root.gameObject.SetActive(false);
        }
    }
}
