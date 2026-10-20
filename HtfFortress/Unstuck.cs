using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HtfFortress
{
    // Out of what you built round yourself (Owen: 「有時候是卡在自己蓋的東西之間 我還沒想到怎麼解好」; the playtest: one shut in
    // until a friend shot him, 「我的人卡到牆上了」, 「我卡死了」). On your own screen, since a player's movement is their own
    // (the game's LocalTeleport takes everyone's view of you along):
    // - inside a piece (one finished where you stood, or the physics pushed you into one): out at once, to the nearest room;
    // - pushing to walk and going nowhere for StuckAfter s, hemmed in (Blocked of eight ways shut, one of them by a piece):
    //   out, through to the far side of what you were pushing at if there's room there, else the nearest room;
    // - holding UnstuckKey for Hold s: the same, toward where you look (shut in by your own walls, with room to walk).
    // Room: something to stand on that's wide enough (not a wall's top), on the island, above the water, and space for you
    // standing; your own level first, then one up (FindFree). F4 (back to the lighthouse) stays for anything else.
    public class Unstuck : MonoBehaviour
    {
        public static ConfigEntry<Key> UnstuckKey;
        public const float StuckAfter = 1.8f, Hold = 1.5f, MoveTolerance = 0.25f, Rest = 3f, InsideFor = 0.35f;
        public const int Blocked = 6;

        // ---------- the rules (pure: tests/Headless) ----------

        // Stuck: inside a piece; or pushing and going nowhere long enough with most ways round shut, one of them by a piece
        // (a wall you walk into shuts three ways, a corner five: neither is being stuck).
        public static bool Stuck(bool inside, bool pushing, float moved, float pushedFor, int blocked, bool byPiece) =>
            inside || pushing && moved < MoveTolerance && pushedFor >= StuckAfter && blocked >= Blocked && byPiece;

        // Where to look, in order. `through`: straight on along each of twelve ways (`dir` first, then swinging out to either
        // side), 0.75-4.5 m, each only worth it past something in the way; then rings round you, nearest first.
        public struct Spot { public Vector2 Offset; public bool Through; }
        private static readonly float[] Rings = { 0.7f, 1.1f, 1.6f, 2.2f, 3f, 4f, 5.5f };
        public static List<Spot> Candidates(Vector2 dir, bool through)
        {
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up;
            float a0 = Mathf.Atan2(dir.y, dir.x);
            Vector2 Way(int i) { int k = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1); float a = a0 + k * Mathf.PI / 6f; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); } // 0, +1, -1, +2, ... 6
            var list = new List<Spot>();
            if (through)
                for (int i = 0; i < 12; i++)
                    for (float d = 0.75f; d <= 4.51f; d += 0.375f) list.Add(new Spot { Offset = Way(i) * d, Through = true });
            foreach (float r in Rings)
                for (int i = 0; i < 12; i++) list.Add(new Spot { Offset = Way(i) * r });
            return list;
        }

        // The first spot that works: `ground` is what you'd stand on looking down from a point (null: nothing fit to stand
        // on), `room` whether you fit standing there, `inWay` whether something's in the way between two points (a through
        // spot needs it: no use going past nothing); at your level (looking from a step above your feet), then a level up.
        public static readonly float[] Levels = { 1.2f, BuildSystem.Cell + 1.2f };
        public static Vector3? FindFree(Vector3 feet, Vector2 dir, bool through, float body,
                                        Func<Vector3, Vector3?> ground, Func<Vector3, bool> room, Func<Vector3, Vector3, bool> inWay)
        {
            var spots = Candidates(dir, through);
            foreach (float up in Levels)
                foreach (var s in spots)
                {
                    if (!(ground(new Vector3(feet.x + s.Offset.x, feet.y + up, feet.z + s.Offset.y)) is Vector3 at)) continue;
                    if (s.Through && !inWay(feet + Vector3.up * body, at + Vector3.up * body)) continue;
                    if (room(at)) return at;
                }
            return null;
        }

        // ---------- on your own screen ----------

        private static readonly AccessTools.FieldRef<PlayerMovement, Rigidbody> Rig = AccessTools.FieldRefAccess<PlayerMovement, Rigidbody>("_rig");
        private static readonly AccessTools.FieldRef<PlayerMovement, CapsuleCollider> Col = AccessTools.FieldRefAccess<PlayerMovement, CapsuleCollider>("_col");
        private static readonly AccessTools.FieldRef<PlayerMovement, Vector2> Input = AccessTools.FieldRefAccess<PlayerMovement, Vector2>("_moveInput");
        private static readonly Collider[] Hits = new Collider[16];
        private static readonly Vector3[] Eight = new Vector3[8];

        private float _held, _readyAt, _pushedFor, _insideSince = -1f, _nextLook;
        private Vector3 _pushFrom;
        private bool _pushing, _broken;

        static Unstuck()
        {
            for (int i = 0; i < 8; i++) Eight[i] = new Vector3(Mathf.Sin(i * Mathf.PI / 4f), 0f, Mathf.Cos(i * Mathf.PI / 4f)); // plain maths: the headless checks run this too
        }

        public static bool IsPiece(Collider c) => c && c.gameObject.name.StartsWith("FortressPiece_", StringComparison.Ordinal);
        private static int Mask => GameInfo.LevelLayer.value; // the island and every piece (they're on its first layer)

        private void Update()
        {
            if (_broken) return;
            try { Tick(); }
            catch (Exception e) { _broken = true; BuildSystem.Log.LogError("Unstuck off: " + e); } // never in the way of playing
        }

        private void Tick()
        {
            var me = Player.LocalPlayer;
            var island = FortressIsland.Instance;
            var mv = me ? me.Movement : null;
            var rig = mv ? Rig(mv) : null;
            var col = mv ? Col(mv) : null;
            if (!rig || !col || !me.Transform || !me.Transform.gameObject.activeInHierarchy || island == null || !island.Exists
                || mv.OnBoat || Ladder.Climbing || (me.Dying && me.Dying.IsDead))
            { Clear(); return; }
            Bounds b = col.bounds;
            Vector3 feet = new Vector3(b.center.x, b.min.y, b.center.z);
            if (!island.OnIsland(feet, FortressIsland.BuildRadius + 8f)) { Clear(); return; }
            float r = Mathf.Max(0.2f, b.extents.x), h = Mathf.Max(r * 2f + 0.2f, b.size.y);
            bool free = !ChatManager.IsTyping && !Hud.ModalOpen && !me.BlockInputs;
            Vector3 look = me.CurPlayerRot * Vector3.forward;

            // The key, held.
            var kb = Keyboard.current;
            if (free && kb != null && UnstuckKey != null && kb[UnstuckKey.Value].isPressed && Time.time >= _readyAt)
            {
                _held += Time.deltaTime;
                Hud.Ring(_held / Hold, L10n.T("unstuckHolding"));
                if (_held >= Hold) { _held = 0f; Free(me, rig, feet, r, h, new Vector2(look.x, look.z), true, "key"); }
                return;
            }
            _held = 0f;
            if (Time.time < _readyAt) return;

            // Inside a piece (a capsule a little inside yours, off the floor: touching isn't inside), for a moment.
            bool inside = false;
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (r + 0.2f), feet + Vector3.up * Mathf.Max(r + 0.2f, h - r - 0.1f), r - 0.08f, Hits, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (Hits[i].attachedRigidbody != rig && IsPiece(Hits[i])) { inside = true; break; }
            if (!inside) _insideSince = -1f;
            else if (_insideSince < 0f) _insideSince = Time.time;
            if (inside && Time.time - _insideSince >= InsideFor) { Free(me, rig, feet, r, h, new Vector2(look.x, look.z), false, "inside"); return; }

            // Pushing and going nowhere.
            Vector2 move = free ? Input(mv) : Vector2.zero;
            bool pushing = move.sqrMagnitude > 0.09f;
            Vector3 flat = feet - _pushFrom; flat.y = 0f;
            if (!pushing || !_pushing || flat.magnitude > MoveTolerance) { _pushFrom = feet; _pushedFor = 0f; }
            else _pushedFor += Time.deltaTime;
            _pushing = pushing;
            if (_pushedFor < StuckAfter || Time.time < _nextLook) return;
            _nextLook = Time.time + 0.25f;
            int blocked = 0;
            bool byPiece = false;
            Vector3 p1 = feet + Vector3.up * (r + 0.2f), p2 = feet + Vector3.up * Mathf.Max(r + 0.2f, h - r - 0.05f);
            foreach (var d in Eight)
                if (Physics.CapsuleCast(p1, p2, r - 0.03f, d, out var hit, 0.35f, Mask, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody != rig)
                { blocked++; byPiece |= IsPiece(hit.collider); }
            if (!Stuck(false, pushing, flat.magnitude, _pushedFor, blocked, byPiece)) return;
            Vector3 push = me.CurPlayerRot * new Vector3(move.x, 0f, move.y);
            Free(me, rig, feet, r, h, new Vector2(push.x, push.z), true, $"pushing, {blocked} of 8 ways shut");
        }

        private void Clear() { _held = 0f; _pushedFor = 0f; _pushing = false; _insideSince = -1f; }

        // Out, if there's anywhere: there in an instant, facing the same way.
        private void Free(Player me, Rigidbody rig, Vector3 feet, float r, float h, Vector2 dir, bool through, string why)
        {
            _readyAt = Time.time + Rest;
            Clear();
            var island = FortressIsland.Instance;
            var at = FindFree(feet, dir, through, h * 0.5f, p => Ground(p, island), p => Room(p, r, h, rig), InWay);
            if (!(at is Vector3 spot))
            {
                Hud.Banner(Hud.Keys(L10n.T("unstuckNowhere")), 3f);
                BuildSystem.Log.LogInfo($"Unstuck ({why}): no room near {feet - island.Center:F1}.");
                return;
            }
            me.LocalTeleport(spot + Vector3.up * (rig.position.y - feet.y + 0.08f), me.CurPlayerRot.eulerAngles.y, true);
            Hud.Banner(L10n.T("unstuckDone"), 1.6f);
            Fx.Sound("PickupGrab", spot, 0.6f, AudioDistance.Short);
            BuildSystem.Log.LogInfo($"Unstuck ({why}): {feet - island.Center:F1} -> {spot - island.Center:F1}.");
            AutoTest.Count(why == "key" ? "unstuck_key" : "unstuck_auto");
        }

        // What you'd stand on under `from`: walkable, wide enough for you (four feet round it land within a step: not the top
        // of a wall, not an edge), on the island, above the water.
        private static Vector3? Ground(Vector3 from, FortressIsland island)
        {
            if (!Physics.Raycast(from, Vector3.down, out var hit, 6f, Mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.55f) return null;
            Vector3 at = hit.point;
            if (at.y < WaterManager.GetWaterHeight(at) + 0.05f || !island.OnIsland(at, FortressIsland.BuildRadius + 10f)) return null;
            for (int i = 0; i < 8; i += 2)
                if (!Physics.Raycast(at + Eight[i] * 0.3f + Vector3.up * 0.5f, Vector3.down, out var foot, 1f, Mask, QueryTriggerInteraction.Ignore) || Mathf.Abs(foot.point.y - at.y) > 0.4f)
                    return null;
            return at;
        }

        // Room for you standing at `feet`: nothing of the island or the fort in your shape (yourself aside).
        private static bool Room(Vector3 feet, float r, float h, Rigidbody self)
        {
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (r + 0.06f), feet + Vector3.up * Mathf.Max(r + 0.06f, h - r), r, Hits, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (Hits[i].attachedRigidbody != self || !self) return false;
            return true;
        }

        private static bool InWay(Vector3 a, Vector3 b) => Physics.Linecast(a, b, Mask, QueryTriggerInteraction.Ignore);
    }
}
