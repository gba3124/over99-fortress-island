using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // What a weapon in hand will reach, drawn on the ground like a MOBA's ability indicators (Owen: "像是lol那種感覺"; "射距…
    // 很多沒設計好": you should see how far each one goes), with the shooter's own range and trait mods:
    // - lobbed (gas, whirlpool, net, jellyfish, puffer) and the rocket: a disc the size of what it does where it comes down;
    // - the shark whistle: the shark comes up on the sea ring in the direction you aim (OnSea) and hunts what's in reach of
    //   the water within its sight of there: a circle of that on the water, a pulsing mark where it surfaces (two for a
    //   legendary's pair);
    // - the conch: its cone round your feet;
    // - everything aimed that flies (bubbles, hook, starfish, boomerang, claw): a ring where the shot stops (the first fish,
    //   or the ground), or at the end of its reach, in red, when you aim past it. Not the laser or the water cannon: their
    //   beam shows where they reach (Owen: "雷射槍為什麼需要落點？").
    // Blue while it's ready, grey while it's cooling down.
    public partial class Weapons
    {
        private Transform _castRing, _castMark, _castMark2, _castCone;
        internal Vector3 DebugCastMark => _castMark && _castMark.gameObject.activeSelf ? _castMark.position : Vector3.zero; // auto-test
        internal Vector3 DebugLastShark => _sharks.Count > 0 ? _sharks[_sharks.Count - 1].Pos : Vector3.zero;
        private Material _castRingMat, _castMarkMat;
        private int _coneHalf = -1;

        private static bool Lobbed(Gear g) => g == Gear.PoisonGas || g == Gear.Whirlpool || g == Gear.NetLauncher || g == Gear.JellyGun || g == Gear.PufferBomb;
        private static bool Aimed(Gear g) => g == Gear.BubbleGun || g == Gear.Hook || g == Gear.StarThrower || g == Gear.Boomerang || g == Gear.LobsterClaw;

        private void CastIndicator(Gear? held, Rarity r)
        {
            var g = held is Gear h ? BarGear(h) : (Gear?)null;
            var cam = GameInfo.CurCamera;
            var me = Player.LocalPlayer;
            bool show = g is Gear k && (k == Gear.SharkWhistle || k == Gear.Rocket || k == Gear.Conch || Lobbed(k) || Aimed(k)) && cam && me && me.Transform
                        && !_drawn && !Hud.ModalOpen && !BuildSystem.Building;
            if (!show)
            {
                foreach (var t in new[] { _castRing, _castMark, _castMark2, _castCone }) if (t && t.gameObject.activeSelf) t.gameObject.SetActive(false);
                return;
            }
            if (!_castRing)
            {
                _castRingMat = BossVfx.Wash(Color.white);
                _castMarkMat = BossVfx.Wash(Color.white);
                _castRing = WeaponFx.Piece(BossVfx.Ring(0f), _castRingMat); // a disc, clear in the middle, solid at its edge
                _castMark = WeaponFx.Piece(BossVfx.Ring(0.35f), _castMarkMat);
                _castMark2 = WeaponFx.Piece(BossVfx.Ring(0.35f), _castMarkMat);
                _castCone = WeaponFx.Piece(Sector(30), _castRingMat);
                _coneHalf = 30;
                foreach (var t in new[] { _castRing, _castMark, _castMark2, _castCone }) { t.name = "FortressCastIndicator"; DontDestroyOnLoad(t.gameObject); }
            }
            Gear gear = g.Value;
            // What the shot is (an absorber copying a weapon fires it at the better of the two rarities) and my mods on it.
            var vault = Loot.Instance ? Loot.Instance.ClientVault : null;
            var copied = held == Gear.Absorber && vault != null ? vault.AbsorbedOf(Loot.MyKey) : null;
            bool legend = (copied != null && Copies(GameInfo.GetSpawnable(copied.Id), out _, out var cr) ? CopyRarity(cr, r) : r) == Rarity.Legendary;
            float reach = WeaponMods.ReachMult(vault != null ? vault.ModOf(Loot.MyKey, gear, WeaponMods.Reach) : 0);
            float trait = WeaponMods.TraitAdd(gear, vault != null ? vault.ModOf(Loot.MyKey, gear, WeaponMods.Trait) : 0);
            _mine.TryGetValue(gear, out var st);
            bool ready = Time.time >= st.readyAt;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            Vector3 o = cam.transform.position, d = cam.transform.forward;
            bool past = false; // aimed past its reach
            Color Tint() => past ? new Color(1f, 0.42f, 0.3f) : ready ? new Color(0.35f, 0.85f, 1f) : new Color(0.62f, 0.64f, 0.68f);
            void Disc(Vector3 at, float radius) { Put(_castRing, at + Vector3.up * 0.08f, new Vector3(radius, 1f, radius)); }
            void Mark(Transform t, Vector3 at, float size) { Put(t, at + Vector3.up * 0.1f, Vector3.one * size * (1f + 0.2f * pulse)); }
            void Off(params Transform[] ts) { foreach (var t in ts) if (t.gameObject.activeSelf) t.gameObject.SetActive(false); }

            if (gear == Gear.SharkWhistle)
            {
                // Where the host will put it (Fire's SharkWhistle: Land, then OnSea), as near as the client can tell.
                Vector3 aim = Reach(o, d, 30f);
                if (Physics.Raycast(aim + Vector3.up * 0.5f, Vector3.down, out var gh, 40f, GameInfo.LevelLayer)) aim = gh.point;
                Vector3 at = OnSea(aim);
                Disc(at, SharkSight * reach);
                Mark(_castMark, at, 1.3f);
                _castMark2.gameObject.SetActive(legend);
                if (legend) Mark(_castMark2, OnSea(aim + Quaternion.Euler(0f, 90f, 0f) * d.Flat().normalized * 4f), 1.3f);
                Off(_castCone);
            }
            else if (Lobbed(gear))
            {
                Vector3 at = Land(o, d, RangeOf(gear) * reach);
                float radius = gear == Gear.PoisonGas ? GasRadius : gear == Gear.Whirlpool ? WhirlRadius : gear == Gear.JellyGun ? JellyRadius
                             : gear == Gear.NetLauncher ? (NetRadius + trait) * (legend ? 1.5f : 1f) : PufferRadius + trait;
                Disc(at, radius);
                Mark(_castMark, at, 0.8f);
                Off(_castMark2, _castCone);
            }
            else if (gear == Gear.Rocket)
            {
                // Fire's Rocket: on the first fish in its way, or where it meets the ground (or the sea).
                Vector3 end = Reach(o, d, RocketRange * reach);
                var first = Along(o, end, LobRadius).FirstOrDefault();
                Vector3 at = first ? first.transform.position : Grounded(end);
                Disc(at, RocketRadius + trait);
                Mark(_castMark, at, 0.8f);
                Off(_castMark2, _castCone);
            }
            else if (gear == Gear.Conch)
            {
                int half = Mathf.RoundToInt(Mathf.Min(80f, ConchHalfAngle + trait));
                if (half != _coneHalf) { var mf = _castCone.GetComponent<MeshFilter>(); Destroy(mf.sharedMesh); mf.sharedMesh = Sector(half); _coneHalf = half; }
                float far = ConchRange * reach;
                Put(_castCone, Grounded(me.Transform.position + Vector3.up) + Vector3.up * 0.08f, new Vector3(far, 1f, far));
                Vector3 fwd = d.Flat();
                if (fwd.sqrMagnitude > 0.001f) _castCone.rotation = Quaternion.LookRotation(fwd);
                Off(_castRing, _castMark, _castMark2);
            }
            else
            {
                // Aimed: Fire's own reach and catch (the claw's first along its line), the ring where it stops.
                float range = RangeOf(gear) * reach;
                Vector3 end = Reach(o, d, range);
                var hits = Along(o, end, gear == Gear.LobsterClaw ? ClawAim : gear == Gear.Boomerang ? BoomerangHitRadius : ShotRadius);
                past = hits.Count == 0 && (end - o).sqrMagnitude >= (range - 0.05f) * (range - 0.05f);
                Mark(_castMark, hits.Count > 0 ? hits[0].transform.position : Grounded(end), 1.1f);
                Off(_castRing, _castMark2, _castCone);
            }
            Color c = Tint();
            _castRingMat.color = new Color(c.r, c.g, c.b, ready ? 0.42f : 0.24f);
            _castMarkMat.color = new Color(c.r, c.g, c.b, (ready ? 0.55f : 0.3f) + 0.3f * pulse);
        }

        private static void Put(Transform t, Vector3 at, Vector3 scale)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t.position = at;
            t.localScale = scale;
        }

        // The ground (or the sea) under a point.
        private static Vector3 Grounded(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var g, 40f, GameInfo.LevelLayer)) p = g.point;
            p.y = Mathf.Max(p.y, WaterManager.GetWaterHeight(p));
            return p;
        }

        // A flat fan `half` degrees either side of +z, radius 1: clear at its point, solid at its edge (like BossVfx.Ring(0)).
        private static Mesh Sector(int half)
        {
            const int n = 24;
            var v = new List<Vector3> { Vector3.zero };
            var c = new List<Color> { new Color(1f, 1f, 1f, 0f) };
            var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(-half, half, i / (float)n);
                v.Add(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
                c.Add(Color.white);
                if (i > 0) t.AddRange(new[] { 0, i, i + 1 });
            }
            var mesh = new Mesh { name = "FortressCone" + half };
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
