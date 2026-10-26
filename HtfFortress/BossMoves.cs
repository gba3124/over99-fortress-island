using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    // What each boss move looks like (BossFx events, keyed "<boss>.<move>"), on every screen. Each is layered from the
    // pieces in BossVfx (flash, ring of shock, chunks, dust, a mark on the ground) in the boss's own colours, with shapes
    // made for it (jagged rock, crystal, spikes, chain links, tentacles); nothing here touches the game state.
    internal static partial class BossMoves
    {
        private static BossFx H;
        private static readonly Color Sand = new Color(0.86f, 0.76f, 0.55f), Basalt = new Color(0.2f, 0.21f, 0.19f), Venom = new Color(0.5f, 1f, 0.3f),
                                      Gold = new Color(1f, 0.8f, 0.3f), Pink = new Color(1f, 0.35f, 0.85f), Violet = new Color(0.22f, 0.06f, 0.28f),
                                      Cyan = new Color(0.4f, 1f, 1f), Crimson = new Color(0.62f, 0.05f, 0.12f), Water = new Color(0.5f, 0.78f, 1f),
                                      Fire = new Color(1f, 0.5f, 0.12f), Blood = new Color(1f, 0.08f, 0.12f), Shadow = new Color(0.35f, 0.02f, 0.16f),
                                      Iron = new Color(0.32f, 0.32f, 0.34f), Stone = new Color(0.55f, 0.55f, 0.52f), Pixel = new Color(0.4f, 1f, 0.6f);

        public static bool Play(BossFx host, FxEvent e)
        {
            H = host;
            Vector3 a = e.From, b = e.To;
            float secs = e.Arg / 1000f, size = e.Who / 100f;
            Color c = BossFx.ColourOf(e.PieceId);
            switch (e.Key)
            {
                // ---- the warden ----
                case "warden.rise": Erupt(a, size, Sand); Sound("SpidercrabWalk_V", 1, 8, a, 1f); Sound("CrabSnip", a); break;
                case "warden.snap":
                    BossVfx.Impact(a, 2.6f, Fire, Sand);
                    BossFx.Burst(a + Vector3.up * 0.8f, "Orange", 40, 0.14f);
                    Sound("MetallicItemImpact_V", 1, 3, a, 1f); Sound("SpiderCrabSnip_V", 1, 3, a, 1f); Shake(a, 0.4f);
                    break;
                case "warden.swing": Sound("SpiderCrabSnip_V", 1, 3, a, 1f); break;
                case "warden.chain": Run(Chain(e.PieceId, e.Who, b, secs)); Sound("MetallicItemImpact_V", 1, 3, b, 0.9f); break;
                case "warden.shell": Run(Shell(e.PieceId, secs, c)); Sound("MetallicItemImpact_V", 1, 3, a, 1f); break;
                case "warden.heat": Run(Charge(e.PieceId, secs, Fire)); Sound("SpiderCrabSnip_V", 1, 3, a, 1f); break;
                case "warden.anvil": Anvil(a, b, size); break;
                // ---- the urchin ----
                case "urchin.land": BossVfx.Impact(a, Mathf.Max(2f, size * 0.8f), Pink, Sand); Shake(a, 0.5f); Sound("ItemImpactHeavy_V", 1, 3, a, 1f); break;
                case "urchin.roll": Run(Roll(e.PieceId, secs)); BossAudio.Sound("PufferfishRoll", a, 1f, AudioDistance.Long); break;
                case "urchin.bristle": Run(Throb(e.PieceId, secs, 0.12f)); Run(Charge(e.PieceId, secs, Pink)); BossAudio.Sound("Pufferfish", a, 1f, AudioDistance.Long); break;
                case "urchin.spike": Pitched("PunchSwoosh", 0, a, 0.8f, 0.8f); Run(Fly(a, b, BossVfx.Spike, BossVfx.Glow(Pink, 3f), new Vector3(1.4f, 1.4f, 1.6f), 0.45f, 4f, Pink, () => { BossVfx.Flash(b + Vector3.up * 0.3f, 0.8f, Pink, 0.2f); BossVfx.Debris(b, 4, Sand, 3f, 0.15f); Pitched("ArrowHitSand_0", 5, b, 1f, 0.8f); })); break;
                case "urchin.caltrop": Run(Caltrops(a, secs)); Pitched("ArrowHitStone_0", 5, a, 0.8f, 1.1f); BossAudio.After(0.08f, () => Pitched("ArrowHitSand_0", 5, a, 0.8f, 1f)); break;
                case "urchin.bloom": Run(Bloom(a, size)); Sound("Slap_0", 1, 3, a, 1f); Sound("ItemImpactHeavy_V", 1, 3, a, 0.8f); break;
                case "urchin.nova":
                    Run(Bloom(a, size, 40, 0.9f));
                    BossVfx.Flash(a + Vector3.up * 1.5f, size * 0.6f, Pink, 0.3f);
                    BossVfx.Shock(a, size * 1.25f, Violet, 0.6f, 0.85f);
                    Shake(a, 0.6f); Sound("Slap_0", 1, 3, a, 1f); Sound("ItemImpactHeavy_V", 1, 3, a, 1f); BossAudio.Sound("Pufferfish", a, 1f, AudioDistance.Long);
                    break;
                // ---- the anglerfish ----
                case "angler.lure": Run(Lure(e.PieceId, secs)); BossAudio.Sound("RadarPing", a, 1f, AudioDistance.Long); break;
                case "angler.flash": LureFlash(a); break;
                case "angler.lunge": BossAudio.Sound("Swallow", a, 1f, AudioDistance.Long); Run(Trail(e.PieceId, secs, WeaponFx.Bubbles, 4)); WeaponFx.Game("WaterSplashWhite", a, Vector3.up); break;
                case "angler.gulp": BossAudio.Sound("Eat_V" + Random.Range(1, 4), a, 1f); WeaponFx.Game("BloodSmall", a, Vector3.up); BossVfx.Flash(a + Vector3.up * 0.6f, 1f, Cyan, 0.15f); break;
                case "angler.blackout": Run(Blackout(e.PieceId, secs)); BossAudio.Sound("LavaWhaleNoise_0" + Random.Range(1, 5), a, 0.8f, AudioDistance.Long); break;
                // ---- the steed ----
                case "steed.splash": WeaponFx.Emit(WeaponFx.Foam, a, 3, Vector3.up); WeaponFx.Emit(WeaponFx.Drops, a, 4, Vector3.up); if (Random.value < 0.25f) Pitched("ItemHitWaterLight_V", 3, a, 0.6f, 0.9f); break; // (sent most frames)
                case "steed.rear": BossAudio.Sound("BoatLandCollisionMedium_V" + Random.Range(1, 4), a, 1f, AudioDistance.Long); BossFx.Burst(a + Vector3.up * 3f, "Gold", 24, 0.14f); break;
                case "steed.land": BossVfx.Shock(a, 4f, Gold, 0.35f); BossVfx.Dust(a, 2f, 6); Sound("ItemImpactHeavy_V", 1, 3, a, 0.9f); break;
                case "steed.charge": Run(Charge(e.PieceId, a, b, secs)); Sound("Footstep_Sand_V", 1, 5, a, 1f); break;
                case "steed.stomp":
                    BossVfx.Impact(a, size * 0.5f, Gold, Sand);
                    BossVfx.Shock(a, size * 1.1f, Gold, 0.6f, 0.85f);
                    BossVfx.Spires(Circle(a, size * 0.75f, 12), 1.8f, Sand * 0.9f, new Color(0f, 0f, 0f, 0f), 0.8f, 0f);
                    Shake(a, 0.6f); Sound("ItemImpactHeavy_V", 1, 3, a, 1f);
                    break;
                case "steed.phantom": Run(Phantom(e.PieceId, a, b, secs)); break;
                case "steed.golden": Run(GoldenSpin(e.PieceId)); Pitched("ShinyCreatureCaught_0", 3, a, 1f, 0.85f); break;
                case "steed.fissure": Run(Fissure(a, b, secs)); Pitched("ItemImpactHeavy_V", 3, a, 1f, 0.6f); Pitched("LavaEruption_01", 0, Vector3.Lerp(a, b, 0.5f), 0.7f, 1.3f, 1.6f); break;
                // ---- the stonefish ----
                case "stone.rise":
                    BossVfx.Spires(Circle(a, size + 1.2f, 9), 2.4f, Basalt, Venom, 1.2f, 0.02f);
                    Erupt(a, size, Basalt); Shake(a, 0.5f); Sound("ItemImpactHeavy_V", 1, 3, a, 1f);
                    break;
                case "stone.dig": Erupt(a, 3f, Basalt); Pitched("ArrowHitSand_0", 5, a, 1f, 0.6f); Pitched("ItemImpactHeavy_V", 3, a, 0.9f, 0.55f); break;
                case "stone.tunnel": Run(Tunnel(a, b, secs)); break;
                case "stone.rumble": Shake(a, 0.3f); BossAudio.Sound("LavaEruption_01", a, 0.6f, AudioDistance.Long); break;
                case "stone.spires": BossVfx.Spires(Line(a, b, 1.4f, 0.5f), 2.8f, Basalt, Venom, 1.8f); BossVfx.Shock(a, 4f, Venom, 0.4f); BossAudio.Sound("LavaEruption_01", a, 0.8f, AudioDistance.Long); break;
                case "stone.spines": Run(Throb(e.PieceId, secs, 0.08f)); Run(Charge(e.PieceId, secs, Venom)); Pitched("Ignite", 0, a, 0.8f, 0.55f); break;
                case "stone.spit": Run(Fly(a, b, WeaponFx.Ball, BossVfx.Glow(Venom, 2.5f), Vector3.one * 0.7f, 0.5f, 4f, Venom, () => { BossVfx.Flash(b + Vector3.up * 0.3f, 1.2f, Venom, 0.2f); WeaponFx.Emit(WeaponFx.Gas, b, 4, Vector3.up); })); Sound("PiranhaVomit_V", 1, 3, a, 0.8f); break;
                case "stone.pool": Run(Pool(a, size, secs)); Pitched("FishSplatHard", 0, a, 1f, 0.6f); Pitched("SizzlingLoop", 0, a, 0.45f, 0.9f, Mathf.Min(secs, 3f)); break;
                case "stone.eyes": Run(Eyes(e.PieceId, secs, Venom)); Pitched("RadarPing", 0, a, 1f, 0.45f); break;
                case "stone.gaze": Run(Gaze(a, b, e.Who)); BossAudio.Sound("Ignite", a, 1f, AudioDistance.Long); break;
                case "stone.petrify": Run(Petrify(e.Who, a, b, secs)); break;
                case "stone.vent": Run(Vent(a, secs)); Pitched("SizzlingLoop", 0, a, 0.7f, 0.75f, secs); break;
                case "stone.geyser": Run(Geyser(a, size)); BossAudio.Sound("LavaEruption_01", a, 0.7f, AudioDistance.Long); Sound("PiranhaVomit_V", 1, 3, a, 0.8f); break;
                // ---- the flying fish ----
                case "sky.pass": BossAudio.Sound("PunchSwoosh", a, 1f, AudioDistance.Long); Shake(a, 0.3f); BossVfx.Shock(a, 6f, Color.white, 0.4f); break;
                case "sky.bomb": Explode(a, 2.6f); break;
                case "sky.call": Sound("Seagull_V", 1, 11, a, 1f); break;
                case "sky.boom": Run(Boom(a, b, secs)); break;
                case "sky.fall": BossAudio.Sound("AlbatrossScreamTakeDamage", a, 1f, AudioDistance.Long); Run(Smoke(e.PieceId, 0.8f)); break;
                case "sky.ring": SonicRing(a, e.Arg / 100f, size); break;
                // ---- the kraken ----
                case "kraken.rise": Run(SeaBurst(a, secs)); break;
                case "kraken.dive": WeaponFx.Game("WaterExplosionWhite", a, Vector3.up); BossVfx.Shock(a, 7f, Water, 0.6f); BossAudio.Sound("ItemHitWaterHeavy_V1", a, 1f, AudioDistance.Long); break;
                case "kraken.tentacles": Run(Tentacles(a, b, secs, Mathf.Max(2, e.Who))); break;
                case "kraken.sweep": Run(SweepFx(a, b, e.Who, Water)); break;
                case "kraken.reach": Run(Reach(e.Who, a, b, secs)); Pitched("BaitLeaveWater_V", 3, a, 1f, 0.6f); Pitched("ItemImpactHeavy_V", 3, a, 0.8f, 1f); break;
                case "kraken.grab": Run(Dragged(e.Who, a, b)); break;
                case "kraken.squeeze": Run(Squeeze(e.Who, a, b)); break;
                case "kraken.ink": Run(Ink(a, size, secs)); break;
                case "kraken.whirl": Run(Whirl(a, size, secs)); break;
                // ---- the voxel fish ----
                case "voxel.load": Run(Load(a, secs)); break;
                case "voxel.in": Cubes(a, 50, true); BossVfx.Flash(a, 2f, Pixel, 0.2f); BossAudio.Sound("TypingClick" + Random.Range(1, 4), a, 1f, AudioDistance.Long); break;
                case "voxel.out": Cubes(a, 50, false); BossAudio.Sound("Error", a, 0.6f, AudioDistance.Long); break;
                case "voxel.copy": Run(Words(a, "Ctrl+C  Ctrl+V", Pixel, secs + 0.8f)); BossAudio.Sound("Typing" + Random.Range(1, 4), a, 1f, AudioDistance.Long); break;
                case "voxel.select": Run(Selection(a, b, size, secs)); Pitched("Select", 0, a, 1f, 0.8f); Pitched("TypingClick", 3, a, 0.8f, 1f); break;
                case "voxel.delete": Run(Pixelate(a, b, size)); BossVfx.Flash(a + Vector3.up, size, Blood, 0.25f); BossAudio.Sound("Error", a, 1f, AudioDistance.Long); break;
                case "voxel.undo":
                    Run(Words(a + Vector3.up * 6f, "Ctrl+Z", new Color(0.4f, 0.8f, 1f), secs)); Run(Rewind(e.PieceId, secs));
                    Pitched("Typing", 3, a, 1f, 1f); BossAudio.After(0.12f, () => Pitched("Typing", 3, a, 1f, 1f)); BossAudio.After(0.25f, () => Pitched("Back", 0, a, 1f, 0.8f));
                    break;
                case "voxel.restored": Cubes(a, 70, true); BossVfx.Flash(a, 3f, new Color(0.4f, 0.8f, 1f), 0.3f); BossAudio.Sound("QuestFinished", a, 0.7f, AudioDistance.Long); break;
                case "voxel.bolt": Pitched("Error", 0, a, 0.6f, 1.5f); Run(Fly(a, b, WeaponFx.Cube, BossVfx.Glow(Random.value < 0.5f ? Pixel : Pink, 4f), Vector3.one * 0.5f, 0.35f, 1.2f, Pixel, () => { Cubes(b, 14, false); BossVfx.Shock(b, 2.5f, Pixel, 0.3f); Pitched("Error", 0, b, 0.8f, 0.8f); Pitched("ItemImpactLight_V", 3, b, 0.8f, 1f); })); break;
                case "voxel.fatal": H.Overlay(OverlayKind.Glitch, secs); BossAudio.Sound("Error", a, 1f, AudioDistance.Long); BossAudio.Sound("RadioNoise_loop", a, 0.8f, AudioDistance.Long); break;
                case "voxel.tile": Run(Tile(a, b, size, secs)); BossAudio.Sound("Error", a, 0.6f, AudioDistance.Long); break;
                // ---- the koi ----
                case "koi.gate": Run(Gate(a, secs)); break;
                case "koi.jet": Run(Jet(a, b, secs, e.Who)); BossAudio.Sound("ItemHitWaterHeavy_V2", a, 1f, AudioDistance.Long); break;
                case "koi.splash":
                    WeaponFx.Game("WaterExplosionWhite", a, Vector3.up); try { VFXManager.Play("WaterExplosion", a); } catch { }
                    BossVfx.Shock(a, size * 2f, Water, 0.6f); BossVfx.Debris(a, 12, Sand, 7f); Ring(a, size, WeaponFx.Foam, 3);
                    Shake(a, 0.6f); BossAudio.Sound("WhaleLanding_0" + Random.Range(1, 4), a, 1f, AudioDistance.Long);
                    break;
                case "koi.call": Sound("ItemHitWaterLight_V", 1, 3, a, 1f); Run(Charge(e.PieceId, secs, Water)); break;
                case "koi.drop": Pitched("PickupWhoosh", 0, a + Vector3.up * 12f, 0.8f, 0.5f); Run(Fly(a + Vector3.up * 30f + Random.insideUnitSphere * 3f, a, WeaponFx.Ball, WeaponFx.Water("BossDrop", new Color(0.55f, 0.8f, 1f, 0.85f)), Vector3.one * 1.3f, 0.6f, 0f, Water, () => { WeaponFx.Game("WaterSplashWhite", a, Vector3.up); WeaponFx.Emit(WeaponFx.Drops, a, 12, Vector3.up); BossVfx.Shock(a, 3f, Water, 0.35f); Pitched("ItemHitWaterHeavy_V", 3, a, 1f, 1f); })); break;
                case "koi.breath": Run(Breath(a, b, secs, e.Who)); BossAudio.Sound("Ignite", a, 0.8f, AudioDistance.Long); break; // short: the loop clip kept sizzling after the breath
                case "koi.burn": Run(Burning(a, secs)); Pitched("Ignite", 0, a, 0.8f, 0.8f); Pitched("FireLoop", 0, a, 0.5f, 1f, Mathf.Min(secs, 2.5f)); break;
                case "koi.charge": Run(Charge(e.PieceId, secs, new Color(0.7f, 0.85f, 1f))); Pitched("UnderwaterOnEnter_0", 2, a, 0.9f, 1.2f, secs); break;
                case "koi.thunder": H.StartCoroutine(BossFx.Lightning(a, new Color(0.75f, 0.85f, 1f))); BossVfx.Impact(a, 2.6f, new Color(0.6f, 0.8f, 1f), Basalt); break;
                case "koi.dragon": Run(Dragon(e.PieceId, secs)); break;
                case "koi.flame": Run(FlameLine(a, b, secs)); BossAudio.Sound("Ignite", a, 0.9f, AudioDistance.Long); BossAudio.Sound("LavaEruption_01", b, 0.5f, AudioDistance.Long); break;
                // ---- the eye ----
                case "eye.descend": Run(Descend(a, secs)); break;
                case "eye.red": H.RedSky(e.Arg > 0); if (BossEye.Of(e.PieceId) is BossEye raging) raging.Rage(e.Arg > 0); if (e.Arg > 0) Pitched("LavaEruption_01", 0, a, 1f, 0.5f, 3f); break;
                case "eye.open":
                    if (BossEye.Of(e.PieceId) is BossEye opening) opening.Flare(secs); // its own lids open wide
                    else Run(Eyes(e.PieceId, secs, Blood));
                    BossAudio.Sound("LavaWhaleNoise_0" + Random.Range(1, 5), a, 1f, AudioDistance.Long);
                    break;
                case "eye.beam": Run(Gaze(e.PieceId, a, b, secs, e.Who)); break;
                case "eye.spears": BossAudio.Sound("BowCharge", a, 1f, AudioDistance.Long); break;
                case "eye.spear": Pitched("PickupWhoosh", 0, a + Vector3.up * 14f, 0.8f, 0.5f); Run(Fly(a + Vector3.up * 35f + Random.insideUnitSphere.Flat() * 4f, a, BossVfx.Shard, BossVfx.Glow(Blood, 4f), new Vector3(0.9f, 3f, 0.9f), 0.55f, 0f, Blood, () => { BossVfx.Impact(a, 1.6f, Blood, Basalt); Pitched("ArrowHitStone_0", 5, a, 1f, 0.7f); Pitched("ItemImpactHeavy_V", 3, a, 0.9f, 0.7f); }, upright: true)); break;
                case "eye.echo": BossVfx.Impact(a, size, Shadow, new Color(0.1f, 0.05f, 0.1f)); WeaponFx.Game("BlackConfetti", a, Vector3.up); Sound("ItemImpactHeavy_V", 1, 3, a, 0.8f); break;
                case "eye.echoLane": Run(Streak(a, b, 0.5f, Shadow, 1.6f)); BossVfx.Impact(b, 2f, Shadow, new Color(0.1f, 0.05f, 0.1f)); Pitched("PunchSwoosh", 0, a, 1f, 0.5f); Pitched("ItemImpactHeavy_V", 3, b, 0.9f, 0.6f); break;
                case "eye.echoCone": Run(SweepFx(a, b, e.Who, Shadow)); break;
                case "eye.pupil":
                    if (BossEye.Of(e.PieceId) is BossEye wide) wide.Flare(secs);
                    Run(SecondPupil(e.PieceId, secs + 0.3f));
                    BossAudio.Sound("LavaWhaleNoise_0" + Random.Range(1, 5), a, 1f, AudioDistance.Long);
                    break;
                case "eye.twin":
                    if (BossEye.Of(e.PieceId) is BossEye twin) twin.Flare(secs);
                    Run(SecondPupil(e.PieceId, secs));
                    Run(Gaze(e.PieceId, a, b, secs, e.Who, true));
                    break;
                case "eye.judgment": Run(Judgment(e.PieceId, a, b, secs, size)); break;
                case "eye.verdict": Verdict(a, size); break;
                default: return false;
            }
            return true;
        }

        private static void Run(IEnumerator e) => H.StartCoroutine(e);
        private static void Sound(string prefix, int min, int max, Vector3 at, float vol) => BossAudio.SoundRandom(prefix, min, max, at, vol, AudioDistance.Long);
        private static void Sound(string clip, Vector3 at) => BossAudio.Sound(clip, at, 1f, AudioDistance.Long);
        // One of `prefix`1..n (n 0: itself) at a pitch, faded out by `max` seconds (0: the whole clip).
        private static void Pitched(string prefix, int n, Vector3 at, float vol, float pitch, float max = 0f) => BossAudio.Pitched(prefix, n, at, vol, pitch, max);

        // ---------- small pieces ----------

        // The screen shakes for whoever is close.
        internal static void Shake(Vector3 at, float amount)
        {
            var me = Player.LocalPlayer;
            if (!me || !me.Transform || !me.ScreenShake) return;
            try { me.ScreenShake.ShakeAt(at, 2, 900f * amount, 8f, 45f); } catch { }
        }

        private static void Ring(Vector3 at, float radius, ParticleSystem ps, int each)
        {
            for (int i = 0; i < 16; i++)
            {
                float t = i * Mathf.PI / 8f;
                WeaponFx.Emit(ps, at + new Vector3(Mathf.Cos(t), 0.2f, Mathf.Sin(t)) * radius, each, Vector3.up);
            }
        }

        // Ground bursting open round a spot: chunks, dust, a ring of shock.
        private static void Erupt(Vector3 at, float radius, Color rock)
        {
            BossVfx.Shock(at, radius * 2.2f, rock * 1.1f, 0.5f);
            BossVfx.Debris(at, 18, rock, 6f, 0.3f);
            BossVfx.Dust(at, radius, 14);
        }

        private static void Explode(Vector3 at, float r)
        {
            WeaponFx.Game(r > 3f ? "BigExplosion" : "SmallExplosion", at + Vector3.up * 0.3f, Vector3.up);
            BossVfx.Impact(at, r, Fire, Basalt);
            BossAudio.Sound("Explosion", at, 0.8f, AudioDistance.Long);
            Shake(at, 0.3f);
        }

        // Points round a circle, and along a line (`step` apart, a little to either side).
        private static List<Vector3> Circle(Vector3 c, float r, int n) => Enumerable.Range(0, n).Select(i => c + Quaternion.Euler(0f, i * 360f / n + Random.Range(-8f, 8f), 0f) * Vector3.forward * r * Random.Range(0.85f, 1.1f)).ToList();
        private static List<Vector3> Line(Vector3 a, Vector3 b, float step, float jitter)
        {
            var pts = new List<Vector3>();
            float len = (b - a).Flat().magnitude;
            Vector3 dir = (b - a).Flat().normalized, side = Vector3.Cross(Vector3.up, dir);
            for (float d = 1.2f; d < len; d += step) pts.Add(a + dir * d + side * Random.Range(-jitter, jitter));
            return pts;
        }

        // Something thrown along an arc (`height` over the middle), turned along its flight (or kept upright: a falling
        // spear), a short glowing tail behind it; `land` when it gets there.
        private static IEnumerator Fly(Vector3 from, Vector3 to, Mesh mesh, Material mat, Vector3 scale, float seconds, float height, Color tail, System.Action land, bool upright = false)
        {
            var t = WeaponFx.Piece(mesh, mat);
            t.localScale = scale;
            var line = WeaponFx.Line("BossTail", BossVfx.Glow(tail, 2f), 0.25f);
            line.positionCount = 6;
            var trail = new Queue<Vector3>();
            Vector3 last = from;
            for (float s = 0f; s < seconds && t; s += Time.deltaTime)
            {
                float k = s / seconds;
                Vector3 p = Vector3.Lerp(from, to, k) + Vector3.up * height * 4f * k * (1f - k);
                t.position = p;
                if (upright) t.rotation = Quaternion.FromToRotation(Vector3.up, (last - p).sqrMagnitude > 1e-6f ? (last - p).normalized : Vector3.up) * Quaternion.Euler(0f, s * 400f, 0f);
                else if ((p - last).sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(p - last);
                trail.Enqueue(p);
                while (trail.Count > 6) trail.Dequeue();
                var pts = trail.ToArray();
                line.positionCount = pts.Length;
                line.SetPositions(pts);
                line.widthMultiplier = 0.25f * scale.x;
                last = p;
                yield return null;
            }
            if (t) Object.Destroy(t.gameObject);
            if (line) Object.Destroy(line.gameObject);
            land?.Invoke();
        }

        // A bright streak drawn from a to b over `seconds`, then fading from its start.
        private static IEnumerator Streak(Vector3 a, Vector3 b, float seconds, Color c, float width = 0.9f)
        {
            var line = WeaponFx.Line("BossStreak", BossVfx.Glow(c, 3f), width);
            line.positionCount = 2;
            a += Vector3.up * 0.6f; b += Vector3.up * 0.6f;
            for (float t = 0f; t < seconds + 0.4f && line; t += Time.deltaTime)
            {
                line.SetPosition(0, Vector3.Lerp(a, b, Mathf.Clamp01((t - seconds) / 0.4f)));
                line.SetPosition(1, Vector3.Lerp(a, b, Mathf.Clamp01(t / Mathf.Max(0.05f, seconds))));
                yield return null;
            }
            if (line) Object.Destroy(line.gameObject);
        }

        // The boss's body throbs (its spikes, its spines).
        private static IEnumerator Throb(int id, float seconds, float amount)
        {
            var body = BossFx.BodyOf(id);
            if (!body) yield break;
            Vector3 s0 = body.localScale;
            for (float t = 0f; t < seconds && body; t += Time.deltaTime) { body.localScale = s0 * (1f + amount * Mathf.Abs(Mathf.Sin(t * 14f))); yield return null; }
            if (body) body.localScale = s0;
        }

        // Power gathering into it: motes of `c` drawn in from all round, faster and faster.
        private static IEnumerator Charge(int id, float seconds, Color c)
        {
            var body = BossFx.BodyOf(id);
            var ps = BossVfx.Sparks(c);
            for (float t = 0f; t < seconds && body; t += 0.05f)
            {
                Vector3 mid = BossLooks.WorldBounds(body).center;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 from = mid + Random.onUnitSphere * Random.Range(3f, 6f);
                    WeaponFx.Emit(ps, from, 1, mid - from, size: 0.14f, velocity: (mid - from) * 2.2f, life: 0.45f);
                }
                yield return new WaitForSeconds(0.05f);
            }
        }

        // Particles behind it while it moves.
        private static IEnumerator Trail(int id, float seconds, ParticleSystem ps, int each)
        {
            var body = BossFx.BodyOf(id);
            for (float t = 0f; t < seconds && body; t += 0.05f)
            {
                WeaponFx.Emit(ps, new Vector3(body.position.x, BossFx.Ground(body.position) + 0.3f, body.position.z) + Random.insideUnitSphere * 1.5f, each, Vector3.up);
                yield return new WaitForSeconds(0.05f);
            }
        }

        // Its eyes glow (two bright points at the front of its head).
        private static IEnumerator Eyes(int id, float seconds, Color c)
        {
            var body = BossFx.BodyOf(id);
            if (!body) yield break;
            var light = new GameObject("BossEyes").AddComponent<Light>();
            light.type = LightType.Point; light.color = c; light.range = 14f; light.shadows = LightShadows.None;
            var l = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Glow(c, 8f));
            var r = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Glow(c, 8f));
            for (float t = 0f; t < seconds && body; t += Time.deltaTime)
            {
                var b = BossLooks.WorldBounds(body);
                Vector3 fwd = body.forward.Flat().normalized, side = Vector3.Cross(Vector3.up, fwd);
                Vector3 front = b.center + fwd * Mathf.Max(b.extents.x, b.extents.z) * 0.8f + Vector3.up * b.extents.y * 0.3f;
                float k = Mathf.Clamp01(t / (seconds * 0.6f)), s = (0.25f + 0.6f * k) * (1f + 0.15f * Mathf.Sin(t * 30f));
                light.transform.position = front;
                light.intensity = 4f * k;
                l.position = front + side * Mathf.Max(0.4f, b.extents.x * 0.25f); l.localScale = Vector3.one * s;
                r.position = front - side * Mathf.Max(0.4f, b.extents.x * 0.25f); r.localScale = Vector3.one * s;
                yield return null;
            }
            Object.Destroy(light.gameObject);
            if (l) Object.Destroy(l.gameObject);
            if (r) Object.Destroy(r.gameObject);
        }

        // A see-through copy of a body as it is now (its skinned meshes baked), placed as it is.
        private static Transform Ghost(Transform body, Material mat)
        {
            var root = new GameObject("BossGhost").transform;
            root.SetPositionAndRotation(body.position, body.rotation);
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r.forceRenderingOff || r is ParticleSystemRenderer || r is LineRenderer) continue; // (hidden: a body under ours)
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer sk) { mesh = new Mesh(); sk.BakeMesh(mesh); }
                else if (r.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;
                if (!mesh) continue;
                var part = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                part.transform.localScale = r is SkinnedMeshRenderer ? Vector3.one : r.transform.lossyScale;
                part.transform.SetParent(root, true);
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                var pr = part.GetComponent<MeshRenderer>();
                if (mat) pr.sharedMaterials = Enumerable.Repeat(mat, mesh.subMeshCount).ToArray(); // a see-through ghost (all of it: our models have a part per colour)
                else pr.sharedMaterials = r.sharedMaterials.Select(m => m && m.shader && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader" ? m : BossVfx.Solid(Iron)).ToArray(); // or a plain copy
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer) part.AddComponent<BossFx.DropMesh>().Mesh = mesh;
            }
            return root;
        }

        // Turrets a cleansing repair tower freed (Fx, a Mend "free"), and when: their chains and stone break there and then.
        private static readonly Dictionary<int, float> Freed = new Dictionary<int, float>();
        public static void Free(int pieceId) => Freed[pieceId] = Time.time;
        private static bool FreedSince(int pieceId, float since) => Freed.TryGetValue(pieceId, out float t) && t >= since;

        // ---------- the warden ----------

        // A chain whips out from its claw to the turret, link by link; a glowing lock snaps shut on it; when the hold is up
        // the chain drops and the lock breaks.
        private static IEnumerator Chain(int bossId, int pieceId, Vector3 to, float seconds)
        {
            var body = BossFx.BodyOf(bossId);
            const int n = 16;
            var mat = BossVfx.Solid(Iron);
            var links = new List<Transform>();
            for (int i = 0; i < n; i++) { var l = WeaponFx.Piece(BossVfx.Link, mat); l.localScale = Vector3.zero; links.Add(l); }
            var lockRing = WeaponFx.Piece(BossVfx.Link, BossVfx.Glow(Fire, 3f));
            lockRing.localScale = Vector3.zero;
            Vector3 top = to + Vector3.up * 1.3f;
            bool locked = false;
            float began = Time.time;
            for (float t = 0f; t < seconds && !FreedSince(pieceId, began); t += Time.deltaTime)
            {
                Vector3 from = body ? body.position + Vector3.up * 1.2f : to;
                float shown = Mathf.Clamp01(t / 0.3f) * n;
                for (int i = 0; i < n; i++)
                {
                    float k = (i + 0.5f) / n;
                    Vector3 p = Vector3.Lerp(from, top, k) + Vector3.down * Mathf.Sin(k * Mathf.PI) * (1.4f + 0.15f * Mathf.Sin(t * 2f + i * 0.4f));
                    Vector3 next = Vector3.Lerp(from, top, k + 1f / n) + Vector3.down * Mathf.Sin((k + 1f / n) * Mathf.PI) * 1.4f;
                    links[i].position = p;
                    links[i].rotation = Quaternion.LookRotation((next - p).sqrMagnitude > 1e-6f ? next - p : Vector3.forward) * Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f) * Quaternion.Euler(90f, 0f, 0f);
                    links[i].localScale = i < shown ? Vector3.one * 0.38f : Vector3.zero;
                }
                if (t >= 0.3f && !locked)
                {
                    locked = true;
                    BossVfx.Flash(top, 1.2f, Fire, 0.2f);
                    BossFx.Burst(top, "Orange", 24, 0.1f);
                    BossAudio.SoundRandom("MetallicItemImpact_V", 1, 3, top, 1f);
                }
                if (locked)
                {
                    lockRing.position = top + Vector3.up * 0.2f;
                    lockRing.rotation = Quaternion.Euler(0f, t * 60f, 0f);
                    lockRing.localScale = Vector3.one * (1.6f + 0.08f * Mathf.Sin(t * 8f));
                }
                yield return null;
            }
            BossFx.Burst(top, "Orange", 20, 0.1f);
            BossVfx.Debris(top, 8, Iron, 4f, 0.15f);
            BossAudio.SoundRandom("MetallicItemImpact_V", 1, 3, top, 0.8f);
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                foreach (var l in links) if (l) { l.position += Vector3.down * 9f * t * Time.deltaTime; l.localScale *= 0.97f; }
                yield return null;
            }
            foreach (var l in links) if (l) Object.Destroy(l.gameObject);
            if (lockRing) Object.Destroy(lockRing.gameObject);
        }

        // Into its shell: an orange dome of force round it, throbbing, a hard glint on its plates; it shatters when it's over.
        private static IEnumerator Shell(int id, float seconds, Color c)
        {
            var body = BossFx.BodyOf(id);
            if (!body) yield break;
            var dome = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Wash(new Color(1f, 0.6f, 0.2f, 0.22f)));
            var mat = dome.GetComponent<MeshRenderer>().sharedMaterial;
            var mats = body.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).SelectMany(r => r.materials).Where(m => m.HasProperty("_MetallicMetallicness")).ToList();
            var was = mats.Select(m => m.GetFloat("_MetallicMetallicness")).ToList();
            foreach (var m in mats) m.SetFloat("_MetallicMetallicness", 1f);
            for (float t = 0f; t < seconds && body; t += Time.deltaTime)
            {
                var b = BossLooks.WorldBounds(body);
                dome.position = b.center;
                dome.localScale = b.size * 1.25f * (1f + 0.03f * Mathf.Sin(t * 6f));
                mat.color = new Color(1f, 0.6f, 0.2f, 0.16f + 0.08f * Mathf.Sin(t * 6f));
                if (Random.value < 0.15f) BossFx.Burst(b.center + Random.onUnitSphere * b.extents.magnitude, "Orange", 2, 0.08f);
                yield return null;
            }
            for (int i = 0; i < mats.Count; i++) if (mats[i]) mats[i].SetFloat("_MetallicMetallicness", was[i]);
            if (dome) { BossVfx.Debris(dome.position, 16, new Color(1f, 0.6f, 0.25f), 6f, 0.2f); Object.Destroy(dome.gameObject); }
            Object.Destroy(mat);
        }

        // The anvil: both claws down at once; iron spikes glowing at the tip burst up in a ring, a shock rolls out over the
        // whole ring, sparks off the claws.
        private static void Anvil(Vector3 at, Vector3 claws, float r)
        {
            BossVfx.Impact(claws, 2.4f, Fire, Sand);
            BossVfx.Shock(at, r * 1.1f, Fire, 0.55f, 0.85f);
            BossVfx.Shock(at, r * 0.75f, Sand, 0.45f);
            BossVfx.Spires(Circle(at, r * 0.8f, 10), 1.4f, Iron, Fire, 0.6f, 0f);
            BossFx.Burst(claws + Vector3.up * 0.8f, "Orange", 50, 0.16f);
            BossVfx.Dust(at, r * 0.8f, 14);
            Shake(at, 0.7f);
            Sound("MetallicItemImpact_V", 1, 3, claws, 1f); Sound("ItemImpactHeavy_V", 1, 3, at, 1f);
        }

        // ---------- the urchin ----------

        // Rolling: a furrow of marks and dust behind it, pink sparks off its spikes.
        private static IEnumerator Roll(int id, float seconds)
        {
            var body = BossFx.BodyOf(id);
            Vector3 lastMark = Vector3.zero;
            for (float t = 0f; t < seconds && body; t += 0.05f)
            {
                Vector3 g = new Vector3(body.position.x, BossFx.Ground(body.position), body.position.z);
                BossVfx.Dust(g, 1.5f, 2);
                if (Random.value < 0.6f) BossFx.Burst(g + Vector3.up * 0.5f, "Pink", 2, 0.1f);
                if ((g - lastMark).sqrMagnitude > 2.5f * 2.5f) { lastMark = g; BossVfx.Scar(g, 1.8f, Pink, 4f); BossVfx.Debris(g, 3, Sand, 4f, 0.2f); }
                yield return new WaitForSeconds(0.05f);
            }
        }

        // Spikes left stuck in the sand, glowing at the tips, sinking away when they're done.
        private static IEnumerator Caltrops(Vector3 at, float seconds)
        {
            var spikes = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                var s = WeaponFx.Piece(BossVfx.Spike, BossVfx.Solid(Violet));
                var tip = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Glow(Pink, 4f), s);
                tip.localPosition = new Vector3(0f, 0f, 1f); tip.localScale = Vector3.one * 0.18f;
                Vector3 p = at + Random.insideUnitSphere.Flat() * 0.8f;
                p.y = BossFx.Ground(p) - 0.15f;
                s.position = p;
                s.rotation = Quaternion.LookRotation(Quaternion.Euler(Random.Range(-35f, 35f), Random.value * 360f, 0f) * Vector3.up);
                s.localScale = new Vector3(1.6f, 1.6f, 1.1f);
                spikes.Add(s);
            }
            yield return new WaitForSeconds(Mathf.Max(0f, seconds - 0.5f));
            for (float t = 0f; t < 0.5f; t += Time.deltaTime) { foreach (var s in spikes) if (s) s.position += Vector3.down * Time.deltaTime * 2f; yield return null; }
            foreach (var s in spikes) if (s) Object.Destroy(s.gameObject);
        }

        // Every spike out at once: a crown of needles shooting out round it, a ring of shock, a flash of pink. (`n` needles
        // rising up to `rise`: the nova's burst goes up as well as out.)
        private static IEnumerator Bloom(Vector3 at, float radius, int n = 22, float rise = 0.35f)
        {
            BossVfx.Flash(at + Vector3.up * 1.5f, 3f, Pink, 0.25f);
            BossVfx.Shock(at, radius * 1.1f, Pink, 0.45f);
            var spikes = new List<(Transform t, Vector3 v)>();
            var mat = BossVfx.Glow(Pink, 3f);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = Quaternion.Euler(0f, i * 360f / n + Random.Range(-6f, 6f), 0f) * new Vector3(0f, Random.Range(0.05f, rise), 1f).normalized;
                var s = WeaponFx.Piece(BossVfx.Spike, mat);
                s.position = at + Vector3.up * 1.5f + d * 1.5f;
                s.rotation = Quaternion.LookRotation(d);
                s.localScale = new Vector3(1.4f, 1.4f, 1.6f);
                spikes.Add((s, d * radius * 2.4f));
            }
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                foreach (var (s, v) in spikes) if (s) { s.position += v * Time.deltaTime; s.localScale *= 0.985f; }
                yield return null;
            }
            foreach (var (s, _) in spikes) if (s) { BossFx.Burst(s.position, "Pink", 2, 0.08f); Object.Destroy(s.gameObject); }
        }

        // ---------- the anglerfish ----------

        // Its lantern: a ball of light at the front of its head, swelling, motes drawn into it.
        private static readonly Dictionary<int, (Transform ball, Light light)> Lanterns = new Dictionary<int, (Transform, Light)>();
        private static IEnumerator Lure(int id, float seconds)
        {
            var body = BossFx.BodyOf(id);
            if (!body) yield break;
            if (!Lanterns.TryGetValue(id, out var l) || !l.ball)
            {
                var ball = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Glow(Cyan, 7f));
                var light = new GameObject("BossLanternLight").AddComponent<Light>();
                light.type = LightType.Point; light.color = Cyan; light.shadows = LightShadows.None;
                light.transform.SetParent(ball, false);
                Lanterns[id] = l = (ball, light);
                H.StartCoroutine(Follow(id, body));
            }
            var motes = BossVfx.Sparks(Cyan);
            for (float t = 0f; t < seconds && l.ball; t += Time.deltaTime)
            {
                float k = t / seconds;
                l.ball.localScale = Vector3.one * (0.6f + 1.6f * k * k) * (1f + 0.06f * Mathf.Sin(t * 25f));
                l.light.intensity = 2f + 6f * k;
                l.light.range = 14f + 24f * k;
                if (Random.value < 0.6f) { Vector3 from = l.ball.position + Random.onUnitSphere * 5f; WeaponFx.Emit(motes, from, 1, l.ball.position - from, size: 0.12f, velocity: (l.ball.position - from) * 2.5f, life: 0.4f); }
                yield return null;
            }
            if (l.ball) { l.ball.localScale = Vector3.one * 0.6f; l.light.intensity = 2.5f; l.light.range = 16f; }
        }
        private static IEnumerator Follow(int id, Transform body)
        {
            while (body && Lanterns.TryGetValue(id, out var l) && l.ball)
            {
                var b = BossLooks.WorldBounds(body);
                l.ball.position = b.center + body.forward * Mathf.Max(b.extents.x, b.extents.z) * 0.95f + Vector3.up * b.extents.y * 1.05f;
                yield return null;
            }
            if (Lanterns.TryGetValue(id, out var gone) && gone.ball) Object.Destroy(gone.ball.gameObject);
            Lanterns.Remove(id);
        }

        // The flash: rings of light burst off the lantern; whoever looks at it is blinded white.
        private static void LureFlash(Vector3 at)
        {
            BossVfx.Flash(at, 4f, Cyan, 0.35f);
            for (int i = 0; i < 3; i++) WeaponFx.Emit(WeaponFx.Rings, at, 1, Random.onUnitSphere, color: Cyan, size: 8f + 5f * i);
            BossFx.Burst(at, "Cyan", 50, 0.15f);
            var me = Player.LocalPlayer;
            var cam = GameInfo.CurCamera;
            if (!me || !cam) return;
            Vector3 to = at - cam.transform.position;
            bool looking = to.magnitude < 45f && Vector3.Angle(cam.transform.forward, to) < 50f && !Physics.Linecast(cam.transform.position, at, GameInfo.LevelLayer);
            H.Overlay(looking ? OverlayKind.White : OverlayKind.Glint, looking ? 1.2f : 0.3f);
            BossAudio.Sound("ShinyCreatureCaught_0" + Random.Range(1, 4), at, 1f, AudioDistance.Long);
        }

        // Blackout: its lantern and its own glow go out, the night goes deeper still (BossFx.Blackout), and the lantern
        // flares back at the end.
        private static IEnumerator Blackout(int id, float seconds)
        {
            var body = BossFx.BodyOf(id);
            if (!body) yield break;
            H.Blackout(seconds);
            Lanterns.TryGetValue(id, out var l);
            var glow = body.Find("BossGlow") is Transform g ? g.GetComponent<Light>() : null;
            if (l.ball) l.ball.gameObject.SetActive(false); // its light is a child of it
            if (glow) glow.enabled = false;
            yield return new WaitForSeconds(seconds);
            if (glow) glow.enabled = true;
            if (!l.ball) yield break;
            l.ball.gameObject.SetActive(true);
            BossVfx.Flash(l.ball.position, 4f, Cyan, 0.3f);
            WeaponFx.Emit(WeaponFx.Rings, l.ball.position, 1, Vector3.up, color: Cyan, size: 9f);
            BossAudio.Sound("ShinyCreatureCaught_0" + Random.Range(1, 4), l.ball.position, 1f, AudioDistance.Long);
        }

        // ---------- the steed ----------

        // The charge: a golden streak down the lane, afterimages of the steed left behind it fading gold, sparks off its hooves.
        private static IEnumerator Charge(int id, Vector3 a, Vector3 b, float seconds)
        {
            var body = BossFx.BodyOf(id);
            H.StartCoroutine(Streak(a, b, seconds, Gold, 1.2f));
            var ghostMat = BossVfx.Wash(new Color(1f, 0.85f, 0.4f, 0.4f));
            var ghosts = new List<(Transform t, float born)>();
            float next = 0f;
            for (float t = 0f; t < seconds + 0.5f; t += Time.deltaTime)
            {
                if (body && t < seconds && t >= next)
                {
                    next = t + 0.08f;
                    ghosts.Add((Ghost(body, ghostMat), t));
                    Vector3 g = new Vector3(body.position.x, BossFx.Ground(body.position), body.position.z);
                    BossFx.Burst(g + Vector3.up * 0.3f, "Gold", 4, 0.1f);
                    BossVfx.Dust(g, 1.2f, 2);
                }
                foreach (var (gt, born) in ghosts) if (gt) gt.localScale = Vector3.one * (1f - Mathf.Clamp01((t - born) / 0.45f) * 0.3f);
                for (int i = ghosts.Count - 1; i >= 0; i--) if (t - ghosts[i].born > 0.45f) { if (ghosts[i].t) Object.Destroy(ghosts[i].t.gameObject); ghosts.RemoveAt(i); }
                yield return null;
            }
            foreach (var (gt, _) in ghosts) if (gt) Object.Destroy(gt.gameObject);
            Object.Destroy(ghostMat);
        }

        // A ghost of the steed, pale and glowing, galloping a lane: gold sparks in its wake.
        private static IEnumerator Phantom(int id, Vector3 a, Vector3 b, float seconds)
        {
            var body = BossFx.BodyOf(id);
            var mat = BossVfx.Wash(new Color(0.85f, 0.92f, 1f, 0.5f));
            var steed = body ? body.GetComponent<BossSteed>() : null;
            var ghost = steed ? steed.Phantom(mat) : body ? Ghost(body, mat) : null; // ours gallops; the game's horse is a still copy, bounced
            Vector3 dir = (b - a).Flat().normalized;
            BossAudio.Sound("Footstep_Sand_V" + Random.Range(1, 6), a, 1f, AudioDistance.Long);
            H.StartCoroutine(Streak(a, b, seconds, new Color(0.7f, 0.85f, 1f), 0.6f));
            if (ghost) { var g0 = ghost.position; ghost.position = new Vector3(a.x, g0.y, a.z); }
            float yOff = ghost && body ? body.position.y - BossLooks.WorldBounds(body).min.y : 0f; // its middle over its hooves
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Vector3 p = Vector3.Lerp(a, b, t / seconds);
                p.y = BossFx.Ground(p) + yOff + (steed ? 0f : Mathf.Abs(Mathf.Sin(t * 12f)) * 0.8f);
                if (ghost) ghost.SetPositionAndRotation(p, Quaternion.LookRotation(dir) * BossLooks.Turn(EnemyKind.Steed));
                if (Random.value < 0.5f) BossFx.Burst(p + Vector3.up, "Cyan", 2, 0.1f);
                BossVfx.Dust(p, 0.6f, 1);
                mat.color = new Color(0.85f, 0.92f, 1f, 0.5f * Mathf.Min(1f, (seconds - t) / 0.3f));
                yield return null;
            }
            if (ghost) Object.Destroy(ghost.gameObject);
            Object.Destroy(mat);
        }

        // The golden spin: three rings of gold turning round it at different tilts until the fight's over.
        private static IEnumerator GoldenSpin(int id)
        {
            var body = BossFx.BodyOf(id);
            if (!body) yield break;
            var rings = new List<Transform>();
            for (int i = 0; i < 3; i++) rings.Add(WeaponFx.Piece(WeaponFx.Torus, BossVfx.Glow(Gold, 4f)));
            BossVfx.Flash(body.position + Vector3.up * 2f, 5f, Gold, 0.4f);
            for (float t = 0f; body; t += Time.deltaTime)
            {
                var b = BossLooks.WorldBounds(body);
                for (int i = 0; i < 3; i++)
                {
                    rings[i].position = b.center;
                    rings[i].rotation = Quaternion.Euler(60f * i + t * 40f, t * (120f + 40f * i), 30f * i);
                    rings[i].localScale = Vector3.one * b.extents.magnitude * (1.5f + 0.15f * i);
                }
                if (Random.value < 0.3f) BossFx.Burst(b.center + Random.onUnitSphere * b.extents.magnitude, "Gold", 1, 0.12f);
                yield return null;
            }
            foreach (var r in rings) if (r) Object.Destroy(r.gameObject);
        }

        // A crack of molten gold split along the ground from the stomp: it runs out in a blink, glows, throws up sparks, and
        // dims and closes at the end.
        private static IEnumerator Fissure(Vector3 a, Vector3 b, float seconds)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt((b - a).Flat().magnitude / 1.2f) + 1, 3, 24);
            Vector3 side = Vector3.Cross(Vector3.up, (b - a).Flat().normalized);
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (n - 1f)) + side * (i == 0 ? 0f : Random.Range(-0.4f, 0.4f));
                p.y = BossFx.Ground(p) + 0.12f;
                pts[i] = p;
            }
            var glow = WeaponFx.Line("BossFissure", BossVfx.Glow(Gold, 4f), 0.6f);
            var core = WeaponFx.Line("BossFissureCore", BossVfx.Glow(new Color(1f, 0.95f, 0.7f), 7f), 0.18f);
            glow.positionCount = core.positionCount = 0;
            var sparks = BossVfx.Sparks(Gold);
            for (float t = 0f; t < seconds && glow && core; t += Time.deltaTime)
            {
                int shown = Mathf.Clamp(Mathf.CeilToInt(n * t / 0.25f), 2, n);
                if (glow.positionCount != shown)
                {
                    glow.positionCount = core.positionCount = shown;
                    for (int i = 0; i < shown; i++) { glow.SetPosition(i, pts[i]); core.SetPosition(i, pts[i]); }
                    BossVfx.Debris(pts[shown - 1], 2, Sand, 4f, 0.15f);
                }
                float fade = Mathf.Clamp01((seconds - t) / 1f);
                glow.widthMultiplier = 0.6f * fade * (0.85f + 0.15f * Mathf.Sin(t * 9f));
                core.widthMultiplier = 0.18f * fade;
                if (Random.value < 0.25f) WeaponFx.Emit(sparks, pts[Random.Range(0, shown)], 1, Vector3.up, size: 0.1f, velocity: Vector3.up * 2.5f, life: 0.5f);
                yield return null;
            }
            if (glow) Object.Destroy(glow.gameObject);
            if (core) Object.Destroy(core.gameObject);
        }

        // ---------- the stonefish ----------

        private static IEnumerator Tunnel(Vector3 a, Vector3 b, float seconds)
        {
            Shake(a, 0.2f);
            for (float t = 0f; t < seconds; t += 0.07f)
            {
                Vector3 p = Vector3.Lerp(a, b, t / seconds);
                p.y = BossFx.Ground(p);
                BossVfx.Dust(p, 1.2f, 2);
                BossVfx.Debris(p, 2, Basalt, 3.5f, 0.2f);
                if (Random.value < 0.2f) BossAudio.SoundRandom("ItemImpactLight_V", 1, 3, p, 0.6f);
                yield return new WaitForSeconds(0.07f);
            }
        }

        // A pool of venom: bubbling, bubbles popping in little green bursts, sinking away at the end.
        private static IEnumerator Pool(Vector3 at, float r, float seconds)
        {
            var go = BossFx.Decal("circle", at, at + Vector3.forward, r, 0, Venom);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var cols = new List<Color>();
            mesh.GetColors(cols);
            for (int i = 0; i < cols.Count; i++) cols[i] = cols[i].a > 0.6f ? new Color(0.7f, 1f, 0.45f, 0.95f) : new Color(0.3f, 0.75f, 0.15f, 0.7f);
            mesh.SetColors(cols);
            var mat = go.GetComponent<MeshRenderer>().sharedMaterial;
            BossVfx.Flash(at + Vector3.up * 0.3f, r, Venom, 0.25f);
            for (float t = 0f; t < seconds && go; t += Time.deltaTime)
            {
                mat.color = new Color(1f, 1f, 1f, (0.75f + 0.15f * Mathf.Sin(t * 4f)) * Mathf.Clamp01((seconds - t) / 0.8f));
                if (Random.value < 0.3f) WeaponFx.Emit(WeaponFx.Bubbles, at + Random.insideUnitSphere.Flat() * r * 0.8f + Vector3.up * 0.1f, 1, Vector3.up, color: new Color(0.6f, 1f, 0.4f));
                if (Random.value < 0.06f) BossVfx.Burst(at + Random.insideUnitSphere.Flat() * r * 0.7f + Vector3.up * 0.15f, Venom, 5, 0.07f);
                yield return null;
            }
            Object.Destroy(mat);
            Object.Destroy(go);
        }

        // Its gaze: a cone of green light swept over the ground for an instant.
        private static IEnumerator Gaze(Vector3 eye, Vector3 far, int half)
        {
            Vector3 g = new Vector3(eye.x, BossFx.Ground(eye), eye.z);
            var go = BossFx.Decal("cone", g, far, 0f, half, Venom);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var cols = new List<Color>();
            mesh.GetColors(cols);
            for (int i = 0; i < cols.Count; i++) cols[i] = new Color(0.6f, 1f, 0.4f, cols[i].a > 0.6f ? 0.9f : 0.45f);
            mesh.SetColors(cols);
            var mat = go.GetComponent<MeshRenderer>().sharedMaterial;
            BossVfx.Flash(eye, 2.5f, Venom, 0.3f);
            for (float t = 0f; t < 0.6f && go; t += Time.deltaTime) { mat.color = new Color(1f, 1f, 1f, 1f - t / 0.6f); yield return null; }
            Object.Destroy(mat);
            Object.Destroy(go);
        }

        // Turned to stone: the gaze strikes the turret in a flash of green; grey stone creeps up it from the ground, part by
        // part, while slabs of rock close in round its foot, venom-green crystals through them; pebbles drop off it while
        // it lasts. At the end the crust bursts, and the stone falls away from the top down, its colours coming back.
        private static IEnumerator Petrify(int pieceId, Vector3 at, Vector3 boss, float seconds)
        {
            var piece = BuildSystem.Instance ? BuildSystem.Instance.PieceObject(pieceId) : null;
            var bounds = piece ? BossLooks.WorldBounds(piece.transform) : new Bounds(at + Vector3.up, new Vector3(1.6f, 2f, 1.6f));
            float began = Time.time;
            H.StartCoroutine(Beam(boss + Vector3.up * 2f, bounds.center, Venom, 0.3f, 0.5f));
            yield return new WaitForSeconds(0.12f);
            BossVfx.Flash(bounds.center, 2.6f, Venom, 0.3f);
            BossFx.Burst(bounds.center, "Green", 30, 0.12f);
            BossAudio.SoundRandom("ItemImpactHeavy_V", 1, 3, at, 0.9f);
            var rends = (piece ? piece.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer) && !(r is LineRenderer)).ToArray() : new Renderer[0])
                .OrderBy(r => r.bounds.min.y).ToArray(); // from its foot up
            var was = rends.Select(r => r.sharedMaterials).ToArray();
            var stone = BossVfx.Solid(Stone);
            Color rock = new Color(0.42f, 0.43f, 0.4f);
            var rockMat = BossVfx.Solid(rock);
            float w = Mathf.Max(0.5f, Mathf.Max(bounds.extents.x, bounds.extents.z)), h = Mathf.Max(1f, bounds.size.y);
            Vector3 foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var crust = new List<(Transform t, Vector3 size, float start)>();
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f + Random.Range(-0.15f, 0.15f);
                Vector3 out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var s = WeaponFx.Piece(BossVfx.Rock(i % 6), rockMat);
                s.position = foot + out_ * w * 0.85f + Vector3.down * 0.1f;
                s.rotation = Quaternion.AngleAxis(Random.Range(18f, 32f), Vector3.Cross(Vector3.up, -out_)) * Quaternion.Euler(0f, Random.value * 360f, 0f); // leaning in on it
                var size = new Vector3(w * Random.Range(0.6f, 0.85f), h * Random.Range(0.45f, 0.7f), w * Random.Range(0.55f, 0.8f));
                s.localScale = Vector3.zero;
                crust.Add((s, size, Random.Range(0f, 0.15f)));
                if (i % 3 == 0)
                {
                    var g = WeaponFx.Piece(BossVfx.Shard, BossVfx.Glow(Venom, 3f), s);
                    g.localPosition = new Vector3(0.18f, 0.5f, 0f);
                    g.localRotation = Quaternion.Euler(0f, 0f, -35f);
                    g.localScale = new Vector3(0.55f, 0.6f, 0.55f);
                }
            }
            int greyed = 0;
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                foreach (var (s, size, start) in crust)
                    if (s) { float k = Mathf.Clamp01((t - start) / 0.35f); s.localScale = size * (1f - (1f - k) * (1f - k)); }
                int due = Mathf.Min(rends.Length, Mathf.CeilToInt(rends.Length * Mathf.Clamp01((t - 0.1f) / 0.6f)));
                for (; greyed < due; greyed++)
                {
                    var r = rends[greyed];
                    if (!r) continue;
                    r.sharedMaterials = Enumerable.Repeat(stone, r.sharedMaterials.Length).ToArray();
                    if (greyed % 2 == 0) BossVfx.Debris(r.bounds.center, 2, Stone, 1.5f, 0.1f);
                }
                yield return null;
            }
            BossAudio.SoundRandom("ItemImpactHeavy_V", 1, 3, at, 0.7f);
            BossVfx.Dust(at, w * 1.2f, 8);
            for (float t = 0.8f; t < seconds - 0.3f && !FreedSince(pieceId, began); t += 0.25f)
            {
                if (Random.value < 0.4f) BossVfx.Debris(bounds.center + Vector3.up * h * 0.3f, 1, Stone, 1.5f, 0.12f);
                if (Random.value < 0.3f) BossFx.Burst(bounds.center + Random.insideUnitSphere * w, "Green", 1, 0.06f);
                yield return new WaitForSeconds(0.25f);
            }
            // It breaks free.
            BossVfx.Flash(bounds.center, 2f, Venom, 0.2f);
            foreach (var (s, size, _) in crust) if (s) BossVfx.Debris(s.position + Vector3.up * size.y * 0.4f, 4, rock, 6f, 0.26f);
            BossVfx.Debris(bounds.center, 12, Stone, 5f, 0.2f);
            BossVfx.Dust(at, w * 1.4f, 12);
            BossVfx.Shock(at, w * 3f, Stone, 0.4f);
            BossAudio.SoundRandom("ItemImpactHeavy_V", 1, 3, at, 1f);
            foreach (var (s, _, _) in crust) if (s) Object.Destroy(s.gameObject);
            for (int i = rends.Length - 1; i >= 0; i--)
            {
                if (rends[i]) rends[i].sharedMaterials = was[i];
                if (i % 2 == 0) yield return null;
            }
        }

        // A beam from a to b, with a bright core in it, thinning out over `seconds`.
        private static IEnumerator Beam(Vector3 a, Vector3 b, Color c, float width, float seconds)
        {
            var glow = WeaponFx.Line("BossBeam", BossVfx.Glow(c, 3f), width * 2.5f);
            var core = WeaponFx.Line("BossBeamCore", BossVfx.Glow(Color.Lerp(c, Color.white, 0.6f), 6f), width);
            foreach (var l in new[] { glow, core }) { l.positionCount = 2; l.SetPosition(0, a); l.SetPosition(1, b); }
            for (float t = 0f; t < seconds && core; t += Time.deltaTime)
            {
                float k = 1f - t / seconds;
                glow.widthMultiplier = width * 2.5f * k;
                core.widthMultiplier = width * k;
                yield return null;
            }
            if (glow) Object.Destroy(glow.gameObject);
            if (core) Object.Destroy(core.gameObject);
        }

        // A venom spring welling up: the ground stained green, bubbles and gas, faster as it's about to blow.
        private static IEnumerator Vent(Vector3 at, float seconds)
        {
            BossVfx.Scar(at, 1.4f, Venom, seconds + 0.3f);
            for (float t = 0f; t < seconds; t += 0.08f)
            {
                float k = t / seconds;
                WeaponFx.Emit(WeaponFx.Bubbles, at + Random.insideUnitSphere.Flat() * 1.2f + Vector3.up * 0.1f, 1 + Mathf.RoundToInt(k * 3f), Vector3.up, color: new Color(0.6f, 1f, 0.4f));
                if (Random.value < 0.2f + 0.5f * k) WeaponFx.Emit(WeaponFx.Gas, at + Vector3.up * 0.3f, 1, Vector3.up, color: new Color(0.45f, 0.8f, 0.3f));
                yield return new WaitForSeconds(0.08f);
            }
        }

        // The spring blows: a column of venom shooting up out of the ground and falling back, drops flung off it, a green
        // blast round its foot.
        private static IEnumerator Geyser(Vector3 at, float r)
        {
            Color c = new Color(0.55f, 1f, 0.35f, 0.55f);
            var col = WeaponFx.Piece(WeaponFx.Cyl, BossVfx.Wash(c));
            var mat = col.GetComponent<MeshRenderer>().sharedMaterial;
            var drops = BossVfx.Sparks(Venom);
            BossVfx.Flash(at + Vector3.up, r * 1.4f, Venom, 0.25f);
            BossVfx.Shock(at, r * 1.6f, Venom, 0.4f);
            WeaponFx.Emit(WeaponFx.Gas, at + Vector3.up * 0.5f, 6, Vector3.up, color: new Color(0.45f, 0.8f, 0.3f));
            for (float t = 0f; t < 0.9f && col; t += Time.deltaTime)
            {
                float k = t / 0.9f, h = 7f * Mathf.Sin(Mathf.Min(1f, k * 2.2f) * Mathf.PI * 0.5f) * (1f - k * k), w = r * 0.9f * (1f - 0.4f * k);
                col.position = at;
                col.localScale = new Vector3(w, Mathf.Max(0.05f, h), w);
                col.rotation = Quaternion.Euler(0f, t * 300f, 0f);
                mat.color = new Color(c.r, c.g, c.b, c.a * (1f - k));
                if (Random.value < 0.6f) WeaponFx.Emit(drops, at + Vector3.up * h, 2, Vector3.up, size: 0.16f, velocity: Random.insideUnitSphere.Flat() * 2.5f + Vector3.up * 3f, life: 0.7f);
                yield return null;
            }
            Object.Destroy(mat);
            if (col) Object.Destroy(col.gameObject);
        }

        // ---------- the flying fish ----------

        // The sonic boom: a white streak, rings of shock bursting along its path one after another, a wall of dust.
        private static IEnumerator Boom(Vector3 a, Vector3 b, float seconds)
        {
            H.StartCoroutine(Streak(a, b, seconds, Color.white, 1.4f));
            BossAudio.Sound("IntroCrash", Vector3.Lerp(a, b, 0.5f), 1f, AudioDistance.Long);
            for (int i = 0; i <= 10; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 10f), g = new Vector3(p.x, BossFx.Ground(p), p.z);
                WeaponFx.Emit(WeaponFx.Rings, p, 1, (b - a).normalized, color: Color.white, size: 9f);
                BossVfx.Shock(g, 5f, Color.white, 0.4f);
                BossVfx.Dust(g, 3f, 5);
                BossVfx.Debris(g, 4, Sand, 6f, 0.2f);
                yield return new WaitForSeconds(seconds / 10f);
            }
            BossAudio.Sound("Explosion", Vector3.Lerp(a, b, 0.5f), 1f, AudioDistance.Long);
            Shake(Vector3.Lerp(a, b, 0.5f), 0.8f);
        }

        // A sonic boom over one spot: the air cracks in white rings rolling out to `outer` (the second one round the first's
        // edge, `inner`), dust thrown up under it, a bang that shakes the screen.
        private static void SonicRing(Vector3 at, float inner, float outer)
        {
            Vector3 g = new Vector3(at.x, BossFx.Ground(at), at.z);
            BossVfx.Shock(g, outer, Color.white, 0.45f, 0.85f);
            BossVfx.Shock(g, outer * 1.15f, new Color(0.75f, 0.9f, 1f), 0.6f, 0.92f);
            for (int i = 0; i < 3; i++) WeaponFx.Emit(WeaponFx.Rings, g + Vector3.up * (2f + 2f * i), 1, Vector3.up, color: Color.white, size: outer * (1.2f + 0.4f * i));
            foreach (var p in Circle(g, Mathf.Lerp(inner, outer, 0.6f), 12)) BossVfx.Dust(p, 1.2f, 2);
            BossVfx.Debris(g, 10, Sand, 6f, 0.2f);
            BossAudio.Sound("IntroCrash", g, 1f, AudioDistance.Long);
            BossAudio.Sound("Explosion", g, 0.7f, AudioDistance.Long);
            Shake(g, 0.7f);
        }

        // Smoke trailing off it as it falls.
        private static IEnumerator Smoke(int id, float seconds)
        {
            var body = BossFx.BodyOf(id);
            for (float t = 0f; t < seconds && body; t += 0.05f)
            {
                WeaponFx.Emit(WeaponFx.Gas, body.position, 2, Vector3.up, color: new Color(0.25f, 0.25f, 0.25f));
                BossFx.Burst(body.position, "Orange", 2, 0.1f);
                yield return new WaitForSeconds(0.05f);
            }
        }

        // ---------- the kraken ----------

        private static IEnumerator SeaBurst(Vector3 at, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.1f)
            {
                WeaponFx.Emit(WeaponFx.Bubbles, at + Random.insideUnitSphere.Flat() * 6f, 6, Vector3.up);
                if (Random.value < 0.3f) WeaponFx.Game("BloodSmall", at + Random.insideUnitSphere.Flat() * 6f, Vector3.up);
                if (Random.value < 0.15f) BossVfx.Shock(at, 8f * Random.value + 3f, Crimson, 0.6f);
                yield return new WaitForSeconds(0.1f);
            }
            WeaponFx.Game("WaterExplosionWhite", at, Vector3.up);
            try { VFXManager.Play("WaterExplosion", at); } catch { }
            BossVfx.Shock(at, 12f, Water, 0.7f);
            BossAudio.Sound("LavaWhaleNoise_0" + Random.Range(1, 5), at, 1f, AudioDistance.Long);
        }

        // A tentacle: a crimson tube from `root` curling up toward `toward` (`rise` 0..1 how far out of the water it is,
        // `lash` 0..1 how far it's come down onto `toward`), with pale suckers along its inside.
        private class Tentacle
        {
            private readonly BossVfx.Tube _tube = new BossVfx.Tube(BossVfx.Solid(new Color(0.55f, 0.06f, 0.12f)));
            private readonly List<Transform> _suckers = new List<Transform>();
            private readonly Vector3[] _path = new Vector3[12];
            public readonly float Length, Seed;

            public Tentacle(float length)
            {
                Length = length;
                Seed = Random.value * 10f;
                for (int i = 0; i < 6; i++) { var s = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Solid(new Color(1f, 0.65f, 0.65f))); _suckers.Add(s); }
            }

            public void Pose(Vector3 root, Vector3 toward, float rise, float lash, float t)
            {
                Vector3 dir = (toward - root).Flat().normalized, side = Vector3.Cross(Vector3.up, dir);
                for (int i = 0; i < _path.Length; i++)
                {
                    float k = i / (float)(_path.Length - 1);
                    // standing up, bent back, its tip curling: brought down toward `toward` as it lashes
                    float up = Mathf.Lerp(Mathf.Sin(k * Mathf.PI * 0.55f) * Length * 0.85f, 0.4f + Mathf.Sin(k * Mathf.PI) * 2f, lash);
                    float out_ = Mathf.Lerp(k * Length * 0.25f - k * k * Length * 0.3f, k * Length, lash);
                    Vector3 sway = side * Mathf.Sin(t * 2.4f + Seed + k * 3f) * k * 1.2f;
                    _path[i] = root + Vector3.up * (up * rise - (1f - rise) * 3f) + dir * out_ + sway;
                }
                _tube.Set(_path, 0.75f, 0.06f);
                for (int i = 0; i < _suckers.Count; i++)
                {
                    int j = 2 + i * 1;
                    Vector3 p = _path[j], next = _path[j + 1];
                    Vector3 inward = Vector3.Cross(next - p, side).normalized;
                    _suckers[i].position = p + inward * Mathf.Lerp(0.7f, 0.1f, j / 11f);
                    _suckers[i].localScale = Vector3.one * Mathf.Lerp(0.38f, 0.12f, j / 11f);
                }
            }

            public void Destroy() { _tube.Destroy(); foreach (var s in _suckers) if (s) Object.Destroy(s.gameObject); }
        }

        // Tentacles up out of the surf along the shore, swaying while it winds up, then lashing down toward b.
        private static IEnumerator Tentacles(Vector3 shore, Vector3 toward, float seconds, int n)
        {
            Vector3 dir = (toward - shore).Flat().normalized, side = Vector3.Cross(Vector3.up, dir);
            var arms = new List<(Tentacle t, Vector3 root)>();
            for (int i = 0; i < n; i++)
            {
                Vector3 root = shore + dir * 3f + side * ((i - (n - 1) * 0.5f) * 3.6f);
                root.y = WaterManager.GetWaterHeight(root) - 0.3f;
                arms.Add((new Tentacle(Random.Range(8f, 10f)), root));
                WeaponFx.Game("WaterSplashWhite", root, Vector3.up);
                BossVfx.Shock(root, 3f, Water, 0.4f);
            }
            BossAudio.Sound("ItemHitWaterHeavy_V" + Random.Range(1, 4), shore, 1f, AudioDistance.Long);
            for (float t = 0f; t < seconds + 0.6f; t += Time.deltaTime)
            {
                float rise = Mathf.Clamp01(t / 0.5f), lash = Mathf.Clamp01((t - seconds) / 0.22f);
                foreach (var (arm, root) in arms) arm.Pose(root, root + dir * 12f, rise, lash, t);
                if (lash >= 1f && t - seconds < 0.25f + Time.deltaTime)
                    foreach (var (_, root) in arms)
                    {
                        Vector3 hit = root + dir * 9f;
                        hit.y = BossFx.Ground(hit);
                        WeaponFx.Game("WaterExplosionWhite", hit, Vector3.up);
                        BossVfx.Impact(hit, 2.2f, Crimson, Sand, false);
                    }
                yield return null;
            }
            foreach (var (arm, _) in arms) arm.Destroy();
        }

        private static IEnumerator SweepFx(Vector3 apex, Vector3 far, int half, Color c)
        {
            Vector3 dir = (far - apex).Flat().normalized;
            float range = (far - apex).Flat().magnitude;
            BossAudio.Sound("WhaleLanding_0" + Random.Range(1, 4), apex, 1f, AudioDistance.Long);
            for (int i = 0; i <= 12; i++)
            {
                Vector3 p = apex + Quaternion.Euler(0f, Mathf.Lerp(-half, half, i / 12f), 0f) * dir * range * 0.75f;
                p.y = BossFx.Ground(p);
                if (p.y <= WaterManager.GetWaterHeight(p) + 0.2f || i % 3 == 0) WeaponFx.Game("WaterExplosionWhite", p, Vector3.up);
                BossVfx.Shock(p, 3f, c, 0.35f);
                BossVfx.Debris(p, 4, Sand, 5f, 0.2f);
                yield return new WaitForSeconds(0.025f);
            }
            Shake(apex, 0.5f);
        }

        // One tentacle up out of the sea beside a turret, its tip coiled round it, squeezing.
        private static readonly Dictionary<int, Tentacle> Holding = new Dictionary<int, Tentacle>();
        private static IEnumerator Reach(int pieceId, Vector3 at, Vector3 shore, float seconds)
        {
            var arm = new Tentacle(9f);
            Holding[pieceId] = arm;
            Vector3 dir = (at - shore).Flat().normalized, root = at - dir * 2.2f;
            root.y = BossFx.Ground(root) - 0.4f;
            BossVfx.Debris(root, 10, Sand, 6f);
            BossVfx.Dust(root, 1.5f, 8);
            for (float t = 0f; t < seconds + 1.4f && Holding.TryGetValue(pieceId, out var held) && held == arm; t += Time.deltaTime)
            {
                arm.Pose(root, at, Mathf.Clamp01(t / 0.4f), Mathf.Clamp01((t - 0.4f) / 0.5f) * 0.85f, t * 3f);
                yield return null;
            }
            // Taken over by the drag (it's no longer ours to clear away), or let go: the move was broken, or it timed out.
            if (Holding.TryGetValue(pieceId, out var mine) && mine == arm) { Holding.Remove(pieceId); arm.Destroy(); }
        }

        // The turret wrenched off the beach (a copy of it: the real one is gone) and dragged under, the tentacle round it.
        private static IEnumerator Dragged(int pieceId, Vector3 at, Vector3 sea)
        {
            var piece = BuildSystem.Instance ? BuildSystem.Instance.PieceObject(pieceId) : null;
            if (Holding.TryGetValue(pieceId, out var arm)) Holding.Remove(pieceId);
            else arm = null;
            Transform copy = null;
            if (piece)
            {
                copy = Ghost(piece.transform, null);
                foreach (var r in piece.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
            BossAudio.SoundRandom("WoodItemImpact_V", 1, 3, at, 1f);
            BossVfx.Debris(at, 14, Sand, 7f);
            Vector3 start = copy ? copy.position : at;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                float k = t / 1.3f;
                Vector3 p = Vector3.Lerp(start, sea + Vector3.down * 3f, k * k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 6f;
                if (copy) { copy.position = p; copy.rotation = Quaternion.Euler(k * 200f, k * 120f, k * 60f); }
                arm?.Pose(sea, p, 1f, 0.85f, t * 3f);
                yield return null;
            }
            WeaponFx.Game("WaterExplosionWhite", sea, Vector3.up);
            BossVfx.Shock(sea, 6f, Water, 0.5f);
            BossAudio.Sound("ItemHitWaterHeavy_V2", sea, 1f, AudioDistance.Long);
            arm?.Destroy();
            if (copy) Object.Destroy(copy.gameObject);
        }

        // The squeeze (Two-Arm Grab): the coil round its target clenches, a crimson burst and splinters, then it lets go and
        // slides back under. The arm is the one Reach put up (taken over from it, like Dragged does).
        private static IEnumerator Squeeze(int key, Vector3 at, Vector3 shore)
        {
            if (!Holding.TryGetValue(key, out var arm)) yield break; // broken: Reach lets it go by itself
            Holding.Remove(key);
            Vector3 dir = (at - shore).Flat().normalized, root = at - dir * 2.2f;
            root.y = BossFx.Ground(root) - 0.4f;
            BossVfx.Impact(at, 1.8f, Crimson, Sand, false);
            WeaponFx.Game("BloodSmall", at + Vector3.up, Vector3.up);
            BossAudio.SoundRandom("WoodItemImpact_V", 1, 3, at, 1f);
            BossAudio.SoundRandom("ItemImpactHeavy_V", 1, 3, at, 0.8f);
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                float k = t / 0.9f;
                arm.Pose(root, at, 1f - Mathf.Clamp01((k - 0.4f) / 0.6f), 0.85f + 0.15f * Mathf.Sin(Mathf.Min(1f, k * 2.5f) * Mathf.PI), t * 3f);
                yield return null;
            }
            arm.Destroy();
        }

        // Ink: splattered black over the beach and a cloud of it; anyone in it has ink all over their eyes.
        private static IEnumerator Ink(Vector3 at, float r, float seconds)
        {
            BossAudio.Sound("PiranhaVomit_V" + Random.Range(1, 4), at, 1f, AudioDistance.Long);
            var splats = new List<(GameObject go, Material m)>();
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = at + Random.insideUnitSphere.Flat() * r * 0.7f;
                var go = BossFx.Decal("circle", p, p + Vector3.forward, Random.Range(1.2f, 2.6f), 0, new Color(0.05f, 0.02f, 0.08f));
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                var cols = new List<Color>();
                mesh.GetColors(cols);
                for (int k = 0; k < cols.Count; k++) cols[k] = new Color(0.03f, 0.01f, 0.05f, 0.85f);
                mesh.SetColors(cols);
                splats.Add((go, go.GetComponent<MeshRenderer>().sharedMaterial));
            }
            bool blinded = false;
            for (float t = 0f; t < seconds; t += 0.1f)
            {
                for (int i = 0; i < 6; i++) WeaponFx.Emit(WeaponFx.Gas, at + Random.insideUnitSphere.Flat() * r + Vector3.up * Random.Range(0.3f, 2.5f), 1, Vector3.up, color: new Color(0.06f, 0.03f, 0.08f));
                foreach (var (go, m) in splats) if (m) m.color = new Color(1f, 1f, 1f, Mathf.Clamp01((seconds - t) / 1f));
                var me = Player.LocalPlayer;
                bool inside = me && me.Transform && (me.Transform.position - at).Flat().magnitude < r;
                if (inside && !blinded) { blinded = true; H.Overlay(OverlayKind.Ink, Mathf.Max(1.5f, seconds - t)); }
                yield return new WaitForSeconds(0.1f);
            }
            foreach (var (go, m) in splats) { if (m) Object.Destroy(m); if (go) Object.Destroy(go); }
        }

        private static IEnumerator Whirl(Vector3 at, float r, float seconds)
        {
            var funnel = WeaponFx.Piece(WeaponFx.Funnel, WeaponFx.Water("BossWhirl", new Color(0.45f, 0.12f, 0.22f, 0.8f), WeaponFx.SpiralTex));
            funnel.position = at;
            var foam = WeaponFx.Orbit(at + Vector3.up * 0.1f, r * 0.7f);
            BossAudio.Sound("UnderwaterLoop", at, 1f, AudioDistance.Long);
            for (float t = 0f; t < seconds && funnel; t += Time.deltaTime)
            {
                float k = Mathf.Min(1f, t / 0.8f) * Mathf.Min(1f, (seconds - t) / 0.8f);
                funnel.localScale = new Vector3(r * 2f * k, 2.4f * k, r * 2f * k);
                funnel.rotation = Quaternion.Euler(0f, -t * 220f, 0f);
                if (Random.value < 0.1f) BossVfx.Shock(at, r * Random.Range(0.6f, 1f), Water, 0.6f);
                yield return null;
            }
            if (funnel) Object.Destroy(funnel.gameObject);
            if (foam) Object.Destroy(foam.gameObject);
        }

        // ---------- the voxel fish ----------

        // Glowing cubes bursting out of a spot (or flying in to it), green and pink.
        private static void Cubes(Vector3 at, int n, bool inward)
        {
            for (int i = 0; i < n; i++)
            {
                var ps = BossVfx.Sparks(Random.value < 0.5f ? Pink : Pixel);
                if (inward) { Vector3 from = at + Random.onUnitSphere * 4f; WeaponFx.Emit(ps, from, 1, at - from, size: 0.35f, velocity: (at - from) * 3f, life: 0.33f); }
                else { Vector3 d = Random.onUnitSphere; WeaponFx.Emit(ps, at + d * 0.5f, 1, d, size: 0.35f, velocity: d * Random.Range(3f, 8f), life: 0.6f); }
            }
        }

        private static IEnumerator Load(Vector3 at, float seconds)
        {
            H.Overlay(OverlayKind.Glitch, 0.6f);
            BossAudio.Sound("RadioNoise_loop", at, 0.8f, AudioDistance.Long);
            H.StartCoroutine(Words(at + Vector3.up * 7f, "NOW LOADING... 99%", Pixel, seconds + 0.4f));
            for (float t = 0f; t < seconds; t += 0.06f) { Cubes(at, 8, true); yield return new WaitForSeconds(0.06f); }
        }

        // Words hanging in the air (always facing the camera), flickering now and then.
        private static IEnumerator Words(Vector3 at, string text, Color c, float seconds)
        {
            if (!Hud.Font) yield break;
            var go = new GameObject("BossWords");
            var t = go.AddComponent<TextMeshPro>();
            t.font = Hud.Font;
            t.text = text;
            t.fontSize = 14f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = c;
            t.fontStyle = FontStyles.Bold;
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(0, 0, 0, 255);
            go.transform.position = at;
            for (float s = 0f; s < seconds && go; s += Time.deltaTime)
            {
                var cam = GameInfo.CurCamera;
                if (cam) go.transform.rotation = Quaternion.LookRotation(go.transform.position - cam.transform.position);
                t.alpha = Mathf.Clamp01(Mathf.Min(s / 0.15f, (seconds - s) / 0.25f)) * (Random.value < 0.08f ? 0.3f : 1f);
                yield return null;
            }
            Object.Destroy(go);
        }

        // The selection: a box of glowing lines over the fort (its corners marked), "DELETE?" over it, blinking faster.
        private static IEnumerator Selection(Vector3 c, Vector3 to, float half, float seconds)
        {
            Vector3 f = (to - c).Flat().normalized, r = Vector3.Cross(Vector3.up, f);
            float y = BossFx.Ground(c);
            var corners = new[] { c + f * half + r * half, c + f * half - r * half, c - f * half - r * half, c - f * half + r * half };
            var lines = new List<LineRenderer>();
            var mat = BossVfx.Glow(new Color(1f, 0.3f, 0.3f), 4f);
            for (int i = 0; i < 4; i++)
                foreach (float h in new[] { 0.15f, 3.2f })
                {
                    var l = WeaponFx.Line("BossSelect", mat, 0.1f);
                    l.positionCount = 2;
                    l.SetPosition(0, new Vector3(corners[i].x, y + h, corners[i].z));
                    l.SetPosition(1, new Vector3(corners[(i + 1) % 4].x, y + h, corners[(i + 1) % 4].z));
                    lines.Add(l);
                }
            for (int i = 0; i < 4; i++)
            {
                var l = WeaponFx.Line("BossSelect", mat, 0.1f);
                l.positionCount = 2;
                l.SetPosition(0, new Vector3(corners[i].x, y + 0.15f, corners[i].z));
                l.SetPosition(1, new Vector3(corners[i].x, y + 3.2f, corners[i].z));
                lines.Add(l);
            }
            H.StartCoroutine(Words(c + Vector3.up * 4.2f, "DELETE?", new Color(1f, 0.3f, 0.3f), seconds));
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                bool on = Mathf.Sin(t * Mathf.Lerp(6f, 30f, t / seconds)) > -0.3f;
                foreach (var l in lines) if (l) l.enabled = on;
                yield return null;
            }
            foreach (var l in lines) if (l) Object.Destroy(l.gameObject);
        }

        // A glitch tile: a square of ground flickering through broken colours, pixels popping off it; whoever stands on it
        // sees the glitch on their own screen.
        private static IEnumerator Tile(Vector3 c, Vector3 to, float half, float seconds)
        {
            var go = BossFx.Decal("box", c, to, half, 0, Pixel);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var mat = go.GetComponent<MeshRenderer>().sharedMaterial;
            var cols = new List<Color>();
            mesh.GetColors(cols);
            var pink = BossVfx.Sparks(Pink);
            var green = BossVfx.Sparks(Pixel);
            Vector3 f = (to - c).Flat().normalized, r = Vector3.Cross(Vector3.up, f);
            bool glitched = false;
            Cubes(c, 24, false);
            for (float t = 0f, next = 0f; t < seconds && go; t += Time.deltaTime)
            {
                if (t >= next)
                {
                    next = t + 0.08f;
                    for (int i = 0; i < cols.Count; i++) { float v = Random.value; cols[i] = v < 0.33f ? new Color(1f, 0f, 0.4f, 0.7f) : v < 0.66f ? new Color(0f, 1f, 0.5f, 0.6f) : new Color(0.2f, 0.4f, 1f, 0.6f); }
                    mesh.SetColors(cols);
                    Vector3 p = c + f * Random.Range(-half, half) + r * Random.Range(-half, half);
                    p.y = BossFx.Ground(p) + 0.2f;
                    WeaponFx.Emit(Random.value < 0.5f ? pink : green, p, 2, Vector3.up, size: 0.3f, velocity: Vector3.up * Random.Range(1.5f, 4f), life: 0.5f);
                }
                mat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Min(t / 0.1f, (seconds - t) / 0.3f)) * (Random.value < 0.1f ? 0.3f : 1f));
                var me = Player.LocalPlayer;
                if (!glitched && me && me.Transform)
                {
                    Vector3 d = (me.Transform.position - c).Flat();
                    if (Mathf.Abs(Vector3.Dot(d, f)) < half && Mathf.Abs(Vector3.Dot(d, r)) < half) { glitched = true; H.Overlay(OverlayKind.Glitch, Mathf.Max(0.5f, seconds - t)); }
                }
                yield return null;
            }
            Object.Destroy(mat);
            if (go) Object.Destroy(go);
        }

        // Everything in the box breaks into pixels flying up.
        private static IEnumerator Pixelate(Vector3 c, Vector3 to, float half)
        {
            Vector3 f = (to - c).Flat().normalized, r = Vector3.Cross(Vector3.up, f);
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 14; j++)
                {
                    Vector3 p = c + f * Random.Range(-half, half) + r * Random.Range(-half, half);
                    p.y = BossFx.Ground(p) + Random.Range(0.1f, 2.6f);
                    WeaponFx.Emit(BossVfx.Sparks(Random.value < 0.5f ? new Color(1f, 0.3f, 0.3f) : Pixel), p, 1, Vector3.up, size: 0.35f, velocity: Vector3.up * Random.Range(2f, 6f) + Random.insideUnitSphere, life: 0.9f);
                }
                yield return new WaitForSeconds(0.05f);
            }
        }

        // Rewinding: a ring of blue cubes turning backwards round it.
        private static IEnumerator Rewind(int id, float seconds)
        {
            var body = BossFx.BodyOf(id);
            var ps = BossVfx.Sparks(new Color(0.4f, 0.8f, 1f));
            for (float t = 0f; t < seconds && body; t += 0.05f)
            {
                var b = BossLooks.WorldBounds(body);
                for (int i = 0; i < 4; i++)
                {
                    float a = -t * 6f + i * Mathf.PI / 2f;
                    Vector3 p = b.center + new Vector3(Mathf.Cos(a), Mathf.Sin(t * 3f + i) * 0.3f, Mathf.Sin(a)) * (b.extents.magnitude + 1f);
                    WeaponFx.Emit(ps, p, 1, Vector3.up, size: 0.3f, velocity: Vector3.zero, life: 0.5f);
                }
                yield return new WaitForSeconds(0.05f);
            }
        }

        // ---------- the koi ----------

        // The dragon gate: a red gate rising out of the sea, a column of water going up through it.
        private static IEnumerator Gate(Vector3 at, float seconds)
        {
            var red = BossVfx.Solid(new Color(0.8f, 0.12f, 0.08f));
            var goldMat = BossVfx.Glow(Gold, 2f);
            var parts = new List<(Transform t, Vector3 pos, Vector3 size)>();
            void Part(Mesh m, Material mat, Vector3 pos, Vector3 size) { var t = WeaponFx.Piece(m, mat); parts.Add((t, pos, size)); }
            Vector3 side = Vector3.Cross(Vector3.up, (at - (FortressIsland.Instance ? FortressIsland.Instance.Center : at + Vector3.forward)).Flat().normalized);
            Part(WeaponFx.Cube, red, side * 4f + Vector3.up * 5f, new Vector3(0.9f, 10f, 0.9f));
            Part(WeaponFx.Cube, red, -side * 4f + Vector3.up * 5f, new Vector3(0.9f, 10f, 0.9f));
            Part(WeaponFx.Cube, red, Vector3.up * 9.6f, new Vector3(11f, 0.7f, 1f));
            Part(WeaponFx.Cube, goldMat, Vector3.up * 10.2f, new Vector3(12f, 0.35f, 1.2f));
            Part(WeaponFx.Cube, red, Vector3.up * 8f, new Vector3(9f, 0.5f, 0.7f));
            var col = WeaponFx.Piece(WeaponFx.Cyl, BossVfx.Wash(new Color(0.55f, 0.82f, 1f, 0.5f)));
            var colMat = col.GetComponent<MeshRenderer>().sharedMaterial;
            BossAudio.Sound("ItemHitWaterHeavy_V3", at, 1f, AudioDistance.Long);
            Quaternion turn = Quaternion.LookRotation(Vector3.Cross(side, Vector3.up));
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float up = Mathf.Min(1f, t / 0.6f), down = Mathf.Min(1f, (seconds - t) / 0.6f), k = up * down;
                foreach (var (p, pos, size) in parts) { p.position = at + pos - Vector3.up * 11f * (1f - k); p.rotation = turn; p.localScale = size; }
                if (col)
                {
                    col.position = at + Vector3.up * 9f * k;
                    col.localScale = new Vector3(3f, 9f * k + 0.01f, 3f);
                    col.rotation = Quaternion.Euler(0f, t * 200f, 0f);
                }
                WeaponFx.Emit(WeaponFx.Foam, at + Random.insideUnitSphere.Flat() * 3f, 2, Vector3.up);
                if (Random.value < 0.5f) WeaponFx.Emit(WeaponFx.Drops, at + Vector3.up * 18f * k, 3, Vector3.up);
                yield return null;
            }
            foreach (var (p, _, _) in parts) if (p) Object.Destroy(p.gameObject);
            if (col) Object.Destroy(col.gameObject);
            Object.Destroy(colMat);
        }

        // A beam of water swept across, white at its heart, spray where it strikes, the ground left wet.
        private static IEnumerator Jet(Vector3 mouth, Vector3 far, float seconds, int sweep)
        {
            var outer = WeaponFx.Line("BossJet", WeaponFx.Water("BossJet", new Color(0.55f, 0.85f, 1f, 0.9f)), 1.6f);
            var core = WeaponFx.Line("BossJetCore", BossVfx.Glow(new Color(0.85f, 0.95f, 1f), 3f), 0.45f);
            outer.positionCount = core.positionCount = 2;
            Vector3 g = new Vector3(mouth.x, BossFx.Ground(mouth), mouth.z), dir = (far - g).Flat().normalized;
            float range = (far - g).Flat().magnitude;
            Vector3 lastMark = Vector3.zero;
            for (float t = 0f; t < seconds && outer; t += Time.deltaTime)
            {
                Vector3 end = g + Quaternion.Euler(0f, Mathf.Lerp(-sweep, sweep, t / seconds), 0f) * dir * range;
                end.y = BossFx.Ground(end);
                outer.SetPosition(0, mouth); outer.SetPosition(1, end);
                core.SetPosition(0, mouth); core.SetPosition(1, end);
                outer.widthMultiplier = 1.6f + 0.2f * Mathf.Sin(t * 40f);
                WeaponFx.Emit(WeaponFx.Drops, end, 6, Vector3.up);
                WeaponFx.Emit(WeaponFx.Foam, end, 1, Vector3.up);
                if ((end - lastMark).sqrMagnitude > 2f * 2f) { lastMark = end; BossVfx.Scar(end, 1.3f, Water, 3f); }
                yield return null;
            }
            if (outer) Object.Destroy(outer.gameObject);
            if (core) Object.Destroy(core.gameObject);
        }

        // Dragon fire: a cone of flame, fire running along the ground, black scorch where it passed.
        private static IEnumerator Breath(Vector3 mouth, Vector3 far, float seconds, int half)
        {
            Vector3 g = new Vector3(mouth.x, BossFx.Ground(mouth), mouth.z), dir = (far - g).Flat().normalized;
            float range = (far - g).Flat().magnitude;
            var fire = BossVfx.Sparks(Fire);
            var hot = BossVfx.Sparks(new Color(1f, 0.85f, 0.3f));
            var light = new GameObject("BossFireLight").AddComponent<Light>();
            light.type = LightType.Point; light.color = Fire; light.range = range; light.shadows = LightShadows.None;
            light.transform.position = g + dir * range * 0.4f + Vector3.up * 2f;
            for (float t = 0f; t < seconds; t += 0.04f)
            {
                light.intensity = 2.5f + Random.value * 1.5f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 d = Quaternion.Euler(0f, Random.Range(-half, half), 0f) * dir;
                    Vector3 to = g + d * range * Random.Range(0.3f, 1f) + Vector3.up * 0.5f;
                    WeaponFx.Emit(Random.value < 0.4f ? hot : fire, mouth, 1, to - mouth, size: Random.Range(0.3f, 0.6f), velocity: (to - mouth) * 1.7f, life: 0.6f);
                }
                if (Random.value < 0.35f) { Vector3 p = g + Quaternion.Euler(0f, Random.Range(-half, half), 0f) * dir * range * Random.Range(0.3f, 1f); p.y = BossFx.Ground(p); WeaponFx.Game("LavaPoof", p, Vector3.up); BossVfx.Scar(p, 1.6f, Fire, 5f); }
                yield return new WaitForSeconds(0.04f);
            }
            Object.Destroy(light.gameObject);
        }

        private static IEnumerator Burning(Vector3 at, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.2f)
            {
                WeaponFx.Emit(BossVfx.Sparks(Fire), at + Vector3.up * 0.8f + Random.insideUnitSphere * 0.6f, 3, Vector3.up, size: 0.18f, velocity: Vector3.up * 2f, life: 0.6f);
                if (Random.value < 0.3f) WeaponFx.Emit(WeaponFx.Gas, at + Vector3.up * 1.5f, 1, Vector3.up, color: new Color(0.2f, 0.2f, 0.2f));
                yield return new WaitForSeconds(0.2f);
            }
        }

        // Dragonfire down a line: a roaring jet from the mouth to the ground just ahead, then along it, its front running out
        // to the far end; white-hot in the middle, scorched ground and flames bursting up where the front passed.
        private static IEnumerator FlameLine(Vector3 mouth, Vector3 far, float seconds)
        {
            var outer = WeaponFx.Line("BossFlame", BossVfx.Glow(Fire, 5f), 2.2f);
            var core = WeaponFx.Line("BossFlameCore", BossVfx.Glow(new Color(1f, 0.9f, 0.55f), 8f), 0.7f);
            outer.positionCount = core.positionCount = 3;
            Vector3 g = new Vector3(mouth.x, BossFx.Ground(mouth), mouth.z), dir = (far - g).Flat().normalized;
            float len = (far - g).Flat().magnitude;
            Vector3 land = g + dir * Mathf.Min(4f, len * 0.3f);
            land.y = BossFx.Ground(land) + 0.5f;
            var fire = BossVfx.Sparks(Fire);
            var light = new GameObject("BossFlameLight").AddComponent<Light>();
            light.type = LightType.Point; light.color = Fire; light.range = len; light.shadows = LightShadows.None;
            Vector3 lastMark = Vector3.zero;
            for (float t = 0f; t < seconds && outer && core; t += Time.deltaTime)
            {
                Vector3 front = g + dir * Mathf.Max(Mathf.Min(4f, len * 0.3f) + 0.5f, len * Mathf.Clamp01(t / 0.35f));
                front.y = BossFx.Ground(front) + 0.5f;
                outer.SetPosition(0, mouth); outer.SetPosition(1, land); outer.SetPosition(2, front);
                core.SetPosition(0, mouth); core.SetPosition(1, land); core.SetPosition(2, front);
                outer.widthMultiplier = 2.2f + 0.4f * Mathf.Sin(t * 45f);
                light.transform.position = Vector3.Lerp(land, front, 0.5f) + Vector3.up * 2f;
                light.intensity = 3f + Random.value * 1.5f;
                WeaponFx.Emit(fire, front, 6, Vector3.up, size: 0.4f, velocity: Vector3.up * 3f + dir * 4f, life: 0.5f);
                if ((front - lastMark).sqrMagnitude > 2.2f * 2.2f) { lastMark = front; BossVfx.Scar(front, 1.5f, Fire, 5f); WeaponFx.Game("LavaPoof", front, Vector3.up); }
                yield return null;
            }
            if (outer) Object.Destroy(outer.gameObject);
            if (core) Object.Destroy(core.gameObject);
            Object.Destroy(light.gameObject);
        }

        // The koi becomes a dragon: thunder, bursts of gold, and it's repainted gold and crimson, glowing.
        private static IEnumerator Dragon(int id, float seconds)
        {
            var body = BossFx.BodyOf(id);
            if (!body) yield break;
            for (int i = 0; i < 4; i++)
            {
                if (!body) yield break;
                H.StartCoroutine(BossFx.Lightning(body.position + Random.insideUnitSphere * 4f, new Color(1f, 0.85f, 0.4f)));
                BossFx.Burst(body.position, "Gold", 40, 0.2f);
                BossVfx.Flash(body.position, 4f + i, Gold, 0.3f);
                yield return new WaitForSeconds(seconds / 5f);
            }
            if (!body) yield break;
            BossLooks.Dress(body, EnemyKind.Koi, true);
            BossVfx.Flash(body.position, 9f, Gold, 0.5f);
            for (int i = 0; i < 3; i++) WeaponFx.Emit(WeaponFx.Rings, body.position, 1, Random.onUnitSphere, color: Gold, size: 14f + 6f * i);
            BossAudio.Sound("BossFightIntro", body.position, 1f, AudioDistance.Long);
            BossAudio.Sound("LavaWhaleNoise_03", body.position, 1f, AudioDistance.Long);
        }

        // ---------- the eye ----------

        private static IEnumerator Descend(Vector3 home, float seconds)
        {
            H.RedSky(true);
            BossAudio.Sound("LavaWhaleNoise_04", home, 1f, AudioDistance.Long);
            for (float t = 0f; t < seconds; t += 0.45f)
            {
                H.StartCoroutine(BossFx.Lightning(home + Random.insideUnitSphere.Flat() * 18f + Vector3.down * 10f, new Color(1f, 0.25f, 0.25f)));
                yield return new WaitForSeconds(0.45f);
            }
            yield return new WaitForSeconds(2.5f);
            H.RedSky(false);
        }

        // Its gaze: a red beam swept over the fort, a white-hot heart in it, a scorched track behind it. `twin` (Second
        // Pupil): a second beam out of the second pupil, swept the other way, the eyeball turned between the two.
        private static IEnumerator Gaze(int id, Vector3 eye, Vector3 far, float seconds, int sweep, bool twin = false)
        {
            var glow = WeaponFx.Line("BossGaze", BossVfx.Glow(Blood, 6f), 2.6f);
            var core = WeaponFx.Line("BossGazeCore", BossVfx.Glow(new Color(1f, 0.85f, 0.8f), 8f), 0.6f);
            glow.positionCount = core.positionCount = 2;
            LineRenderer glow2 = null, core2 = null;
            if (twin)
            {
                glow2 = WeaponFx.Line("BossGaze", BossVfx.Glow(Blood, 6f), 2.6f);
                core2 = WeaponFx.Line("BossGazeCore", BossVfx.Glow(new Color(1f, 0.85f, 0.8f), 8f), 0.6f);
                glow2.positionCount = core2.positionCount = 2;
            }
            var body = BossFx.BodyOf(id);
            var seer = body ? body.GetComponent<BossEye>() : null;
            var sparks = BossVfx.Sparks(Fire);
            Vector3 g = new Vector3(eye.x, BossFx.Ground(eye), eye.z), dir = (far - g).Flat().normalized;
            float range = (far - g).Flat().magnitude;
            BossAudio.Sound("Ignite", far, 0.7f, AudioDistance.Long); // a short sizzle, not the grill loop ringing out over the fort
            Vector3 lastMark = Vector3.zero, lastMark2 = Vector3.zero;
            for (float t = 0f; t < seconds && core; t += Time.deltaTime)
            {
                Vector3 end = g + Quaternion.Euler(0f, Mathf.Lerp(-sweep, sweep, t / seconds), 0f) * dir * range;
                end.y = BossFx.Ground(end);
                Vector3 end2 = g + Quaternion.Euler(0f, Mathf.Lerp(sweep, -sweep, t / seconds), 0f) * dir * range;
                end2.y = BossFx.Ground(end2);
                if (seer) seer.LookAt(twin ? Vector3.Lerp(end, end2, 0.5f) : end, 0.2f);
                Vector3 from = seer ? seer.Pupil : body ? body.position : eye;
                glow.SetPosition(0, from); glow.SetPosition(1, end);
                core.SetPosition(0, from); core.SetPosition(1, end);
                glow.widthMultiplier = 2.2f + 0.5f * Mathf.Sin(t * 40f);
                WeaponFx.Emit(sparks, end, 4, Vector3.up, size: 0.14f);
                BossVfx.Dust(end, 0.6f, 1);
                if ((end - lastMark).sqrMagnitude > 1.8f * 1.8f) { lastMark = end; BossVfx.Scar(end, 1.2f, Blood, 6f); }
                if (twin && core2)
                {
                    Vector3 from2 = seer ? seer.PupilAt(SecondPupilYaw) : from;
                    glow2.SetPosition(0, from2); glow2.SetPosition(1, end2);
                    core2.SetPosition(0, from2); core2.SetPosition(1, end2);
                    glow2.widthMultiplier = 2.2f + 0.5f * Mathf.Sin(t * 40f + 1.7f);
                    WeaponFx.Emit(sparks, end2, 4, Vector3.up, size: 0.14f);
                    if ((end2 - lastMark2).sqrMagnitude > 1.8f * 1.8f) { lastMark2 = end2; BossVfx.Scar(end2, 1.2f, Blood, 6f); }
                }
                yield return null;
            }
            if (glow) Object.Destroy(glow.gameObject);
            if (core) Object.Destroy(core.gameObject);
            if (glow2) Object.Destroy(glow2.gameObject);
            if (core2) Object.Destroy(core2.gameObject);
        }

        // The second pupil: a red iris with a black slit in it, opening on the eyeball beside the first and held open until
        // its time is up (the beams' event keeps it open longer; broken, it shuts after the wind-up).
        private const float SecondPupilYaw = 32f;
        private static readonly Dictionary<int, float> Pupils = new Dictionary<int, float>(); // boss id -> open until
        private static IEnumerator SecondPupil(int id, float seconds)
        {
            bool open = Pupils.ContainsKey(id);
            Pupils[id] = Mathf.Max(open ? Pupils[id] : 0f, Time.time + seconds);
            if (open) yield break; // already showing: just held open longer
            var eye = BossEye.Of(id);
            if (!eye) { Pupils.Remove(id); yield break; }
            var iris = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Glow(Blood, 6f));
            var slit = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Solid(new Color(0.04f, 0.01f, 0.02f)));
            var rim = WeaponFx.Piece(WeaponFx.Torus, BossVfx.Glow(new Color(1f, 0.85f, 0.75f), 8f));
            float k = 0f, t = 0f;
            while (eye && Pupils.TryGetValue(id, out float until) && (Time.time < until || k > 0.01f))
            {
                k = Mathf.MoveTowards(k, Time.time < until ? 1f : 0f, Time.deltaTime / (Time.time < until ? 0.5f : 0.25f));
                float s = Mathf.SmoothStep(0f, 1f, k) * (1f + 0.06f * Mathf.Sin(t * 20f));
                Vector3 p = eye.PupilAt(SecondPupilYaw), n = (p - eye.Middle).normalized;
                Quaternion face = Quaternion.LookRotation(n);
                iris.SetPositionAndRotation(p - n * 0.1f, face); iris.localScale = new Vector3(1.1f, 1.1f, 0.14f) * s;
                slit.SetPositionAndRotation(p - n * 0.05f, face); slit.localScale = new Vector3(0.35f, 0.8f, 0.16f) * s;
                rim.SetPositionAndRotation(p - n * 0.08f, face); rim.localScale = Vector3.one * 1.3f * s;
                t += Time.deltaTime;
                yield return null;
            }
            Pupils.Remove(id);
            if (iris) Object.Destroy(iris.gameObject);
            if (slit) Object.Destroy(slit.gameObject);
            if (rim) Object.Destroy(rim.gameObject);
        }

        // The Last Judgment: a red sun swelling over the lighthouse, a beam from the eye into it, lightning all round (out to
        // `field`: the whole island).
        private static IEnumerator Judgment(int id, Vector3 eye, Vector3 core, float seconds, float field)
        {
            field = Mathf.Max(12f, field);
            H.RedSky(true);
            BossAudio.Sound("BossFightIntro", core, 1f, AudioDistance.Long);
            var sun = WeaponFx.Piece(WeaponFx.Ball, BossVfx.Glow(Blood, 5f));
            var beam = WeaponFx.Line("BossJudgment", BossVfx.Glow(Blood, 6f), 0.2f);
            beam.positionCount = 2;
            var body = BossFx.BodyOf(id);
            Vector3 over = core + Vector3.up * 9f;
            for (float t = 0f; t < seconds && sun; t += Time.deltaTime)
            {
                float k = t / seconds;
                sun.position = over;
                sun.localScale = Vector3.one * (0.5f + 6f * k * k) * (1f + 0.04f * Mathf.Sin(t * 30f));
                var seer = body ? body.GetComponent<BossEye>() : null;
                if (seer) seer.LookAt(over, 0.2f);
                beam.SetPosition(0, seer ? seer.Pupil : body ? body.position : eye);
                beam.SetPosition(1, over);
                beam.widthMultiplier = 0.2f + 1.6f * k;
                if (Random.value < 0.08f + 0.3f * k) H.StartCoroutine(BossFx.Lightning(core + Random.insideUnitSphere.Flat() * field, new Color(1f, 0.2f, 0.2f)));
                if (Random.value < 0.3f) { Vector3 from = over + Random.onUnitSphere * 10f; WeaponFx.Emit(BossVfx.Sparks(Blood), from, 1, over - from, size: 0.2f, velocity: (over - from) * 2f, life: 0.5f); }
                yield return null;
            }
            if (sun) Object.Destroy(sun.gameObject);
            if (beam) Object.Destroy(beam.gameObject);
        }

        private static void Verdict(Vector3 core, float field)
        {
            try { VFXManager.Play("LavaExplosion", core); VFXManager.Play("VolcanoEruption", core); } catch { }
            WeaponFx.Game("BiggestExplosion", core + Vector3.up * 2f, Vector3.up);
            BossVfx.Impact(core, 6f, Blood, Basalt);
            BossVfx.Shock(core, Mathf.Max(28f, field), Blood, 0.9f, 0.85f);
            // the rest of the island struck too: bolts and blasts scattered over it
            for (int i = 0; i < (field > 12f ? 8 : 0); i++)
            {
                Vector3 p = core + Random.insideUnitSphere.Flat() * field * 0.85f;
                p.y = BossFx.Ground(p);
                H.StartCoroutine(BossFx.Lightning(p, new Color(1f, 0.2f, 0.2f)));
                BossVfx.Impact(p, 2f, Blood, Basalt);
            }
            BossAudio.Sound("LavaWhaleDeathExplosion", core, 1f, AudioDistance.Long);
            Shake(core, 1f);
            H.Overlay(OverlayKind.White, 0.6f);
        }
    }

    public enum OverlayKind { White, Glint, Ink, Glitch }

    public partial class BossFx
    {
        // ---------- on the screen ----------

        private Canvas _canvas;
        private Image _overlay;
        private readonly List<Image> _bars = new List<Image>();

        // A wash over this player's screen: a white flash, ink splatter, a glitch.
        public void Overlay(OverlayKind kind, float seconds) => StartCoroutine(OverlayRun(kind, seconds));
        private IEnumerator OverlayRun(OverlayKind kind, float seconds)
        {
            if (!_canvas)
            {
                Hud.Canvas("Over99BossScreen", 850, ref _canvas);
                var rt = Hud.Node(_canvas.transform, "Wash", Vector2.zero, Vector2.zero, Vector2.zero);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                _overlay = rt.gameObject.AddComponent<Image>();
                _overlay.raycastTarget = false;
                for (int i = 0; i < 10; i++)
                {
                    var bar = Hud.Node(_canvas.transform, "Glitch", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 30f));
                    var img = bar.gameObject.AddComponent<Image>();
                    img.raycastTarget = false;
                    _bars.Add(img);
                }
            }
            _canvas.gameObject.SetActive(true);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float fade = Mathf.Clamp01((seconds - t) / Mathf.Min(0.6f, seconds * 0.5f));
                switch (kind)
                {
                    case OverlayKind.White: _overlay.color = new Color(1f, 1f, 1f, 0.95f * fade); break;
                    case OverlayKind.Glint: _overlay.color = new Color(0.7f, 1f, 1f, 0.35f * fade); break;
                    case OverlayKind.Ink: _overlay.color = new Color(0.03f, 0.02f, 0.05f, 0.82f * fade * (0.9f + 0.1f * Mathf.Sin(t * 5f))); break;
                    default:
                        _overlay.color = new Color(Random.value * 0.2f, Random.value * 0.4f, Random.value * 0.2f, 0.15f * fade);
                        foreach (var b in _bars)
                        {
                            b.gameObject.SetActive(Random.value < 0.6f);
                            b.rectTransform.anchoredPosition = new Vector2(Random.Range(-200f, 200f), Random.Range(-600f, 600f));
                            b.rectTransform.sizeDelta = new Vector2(4000f, Random.Range(6f, 60f));
                            b.color = Random.value < 0.33f ? new Color(1f, 0f, 0.4f, 0.5f * fade) : Random.value < 0.5f ? new Color(0f, 1f, 0.5f, 0.5f * fade) : new Color(0.2f, 0.4f, 1f, 0.5f * fade);
                        }
                        break;
                }
                yield return null;
            }
            _overlay.color = Color.clear;
            foreach (var b in _bars) b.gameObject.SetActive(false);
        }

        // A bolt from the sky to `at`: a jagged glowing line, a flash, thunder.
        public static IEnumerator Lightning(Vector3 at, Color c)
        {
            Vector3 top = at + Vector3.up * 45f + Random.insideUnitSphere.Flat() * 6f;
            var line = WeaponFx.Line("BossLightning", BossVfx.Glow(c, 9f), 0.35f);
            line.positionCount = 9;
            for (int i = 0; i < 9; i++) line.SetPosition(i, Vector3.Lerp(top, at, i / 8f) + (i > 0 && i < 8 ? Random.insideUnitSphere * 2.2f : Vector3.zero));
            var flash = new GameObject("BossLightningFlash").AddComponent<Light>();
            flash.type = LightType.Point; flash.color = c; flash.range = 60f; flash.intensity = 6f; flash.transform.position = at + Vector3.up * 8f;
            WeaponFx.Emit(WeaponFx.Sparks("Cyan"), at, 20, Vector3.up, color: c, size: 0.1f);
            BossAudio.Sound("Explosion", at, 0.5f, AudioDistance.Long);
            yield return new WaitForSeconds(0.08f);
            if (line) line.enabled = false;
            yield return new WaitForSeconds(0.05f);
            if (line) line.enabled = true;
            yield return new WaitForSeconds(0.1f);
            if (line) Object.Destroy(line.gameObject);
            if (flash) Object.Destroy(flash.gameObject);
        }
    }
}
