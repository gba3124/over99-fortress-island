using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HtfFortress
{
    // The island's entrance, and the last scene of a won run.
    // The first time the island shows (the final boss's quest just handed in, before the player goes to roll the game's
    // credits; or the mod first finding it there), the camera leaves the player for the sea: the water boils, the island rises out of it, the lighthouse lights up and the
    // title comes up, OVER 99; then a fade back to the player. When wave 100 falls, the camera circles the lighthouse
    // through the fireworks instead. Space or Esc skips. The camera is the game's own death camera (set up like the
    // player's), borrowed while the player is alive.
    public class Reveal : MonoBehaviour
    {
        public static Reveal Instance;
        public static ConfigEntry<bool> Seen; // the entrance has played on this computer (F10 replays it)
        public static bool Playing { get; private set; }

        private const float Depth = 12f;       // how far under the sea the island starts (its cliffs reach about 9 m up)
        private bool _queued, _skip;
        // Skipped only by holding Space (or Enter) this long: a tap is a jump in the middle of a fight, not "skip".
        private const float SkipHold = 3f;
        private float _held;
        private Image _skipTrack, _skipFill;
        private float _calmSince = -1f;
        private Camera _cam;
        private float _fov, _camFov;
        private Canvas _canvas;
        private Image _fade, _barTop, _barBottom;
        private CanvasGroup _card;
        private TextMeshProUGUI _title, _sub, _line;
        private readonly List<Canvas> _hidden = new List<Canvas>();
        private readonly List<Rigidbody> _frozen = new List<Rigidbody>();
        private readonly List<Renderer> _unseen = new List<Renderer>();
        private float _fog = -1f;
        private readonly List<GameObject> _props = new List<GameObject>(); // the light, beams and flash while it plays
        // A boss's entrance, JoJo style: ゴゴゴ round the edges, speed lines, a flash in its colour, its name card.
        private RectTransform _menaceRoot, _linesRoot, _bossCardRt;
        private readonly List<TextMeshProUGUI> _gogo = new List<TextMeshProUGUI>();
        private readonly List<(RectTransform rt, Image img)> _streaks = new List<(RectTransform, Image)>();
        private Image _flashImg;
        private float _flashAt = -10f;
        private Color _flashColour;
        private CanvasGroup _bossCard;
        private TextMeshProUGUI _bossEpithet, _bossName, _bossQuote, _bossWave;

        private void Awake() => Instance = this;

        // The island just appeared (the final boss's quest was handed in): play once the player is free.
        public void Queue() => _queued = true;

        // The game's ending: the credits, and the black screen it fades back out of afterwards (about 6 s more).
        private static readonly System.Reflection.FieldInfo EndUi = AccessTools.Field(typeof(EndGameUI), "_instance"),
                                                             EndCanvas = AccessTools.Field(typeof(EndGameUI), "_endGameCanvas");
        private static bool Ending()
        {
            if (EndGameUI.IsShowingEndGame || EndGameEffects.IsShowingEndGame) return true;
            var ui = EndUi?.GetValue(null);
            return ui != null && EndCanvas?.GetValue(ui) is GameObject c && c && c.activeInHierarchy;
        }
        public void Replay() { if (!Playing) StartCoroutine(Rise()); }

        // Auto-test: a clean camera (no HUD, no hands) at `eye` looking at `look`, until TestViewEnd.
        public bool TestView(Vector3 eye, Vector3 look)
        {
            if (!Playing && !Begin(Player.LocalPlayer)) return false;
            Aim(eye, Quaternion.LookRotation(look - eye));
            _cam.fieldOfView = 55f;
            return true;
        }
        public void TestViewEnd() { if (Playing) StartCoroutine(Finish(Player.LocalPlayer, null)); }
        public void Skip() => _skip = true;

        private void Update()
        {
            var kb = Keyboard.current;
            if (Playing)
            {
                bool holding = kb != null && (kb.spaceKey.isPressed || kb.enterKey.isPressed);
                _held = holding ? _held + Time.unscaledDeltaTime : 0f;
                if (_held >= SkipHold) _skip = true;
                if (_skipTrack)
                {
                    _skipTrack.gameObject.SetActive(_held > 0f);
                    _skipFill.rectTransform.sizeDelta = new Vector2(160f * Mathf.Clamp01(_held / SkipHold), 6f);
                }
                return;
            }
            var island = FortressIsland.Instance;
            var me = Player.LocalPlayer;
            if (Ending()) { _calmSince = -1f; return; } // never over the game's credits
            if ((!_queued && Seen.Value) || island == null || !island.Exists) { _calmSince = -1f; return; }
            // Wait until the player has been free for a moment: in the world, alive, no menu, no round, no boss.
            bool calm = me && me.Transform && !me.Dying.IsDead && !Hud.ModalOpen && !PauseManager.IsPaused
                        && !(Siege.Instance && Siege.Instance.ViewActive) && !BossManager.Boss && !Boat.IsDrivingLocally && !SelfDrivingBoat.IsDriving && !AutoTest.Running;
            if (!calm) { _calmSince = -1f; return; }
            if (_calmSince < 0f) _calmSince = Time.time;
            if (Time.time - _calmSince < (_queued ? 2f : 5f)) return;
            _calmSince = -1f;
            bool queued = _queued;
            _queued = false;
            if (island.OnIsland(me.Transform.position, FortressIsland.IslandRadius + 30f)) // already there: nothing to reveal
            {
                Seen.Value = true;
                if (queued) Hud.Banner(L10n.T("islandUnlocked"), 6f);
                return;
            }
            StartCoroutine(Rise());
        }

        // ---------- the entrance ----------

        private IEnumerator Rise()
        {
            var me = Player.LocalPlayer;
            var island = FortressIsland.Instance;
            if (island == null || !island.Exists || !Begin(me)) { Hud.Banner(L10n.T("islandUnlocked"), 8f); yield break; }
            Seen.Value = true;
            AutoTest.Count("reveal_started");
            Vector3 c = island.Center, eye = _cam.transform.position;
            Quaternion eyeRot = _cam.transform.rotation;
            Vector3 dir = eye - c;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.back;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 look = c + Vector3.up * 3f;
            Vector3 far = c + dir * 72f + side * 12f + Vector3.up * 22f, close = c + dir * 44f - side * 16f + Vector3.up * 11f;
            bool rise = !island.OnIsland(me.Transform.position, FortressIsland.IslandRadius + 5f); // (F10 on the island: it can't sink under you)
            if (rise) { Freeze(island); island.Sink = Depth; }

            // Out over the sea, in an arc.
            yield return Over(2.8f, k =>
            {
                float e = Ease(k);
                Vector3 p = Vector3.Lerp(eye, far, e) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 14f;
                Aim(p, Quaternion.Slerp(eyeRot, Quaternion.LookRotation(look - p), Ease(Mathf.Clamp01(k * 1.3f))));
                _cam.fieldOfView = Mathf.Lerp(_fov, 55f, e);
            });
            if (AutoTest.Running) FortressPlugin.Shot("reveal_sea");

            // The sea boils and the island comes up.
            if (!_skip) Sting("BossFightIntro", 0.9f);
            float nextSplash = 0f, nextRumble = 0f;
            int shots = 0;
            yield return Over(rise ? 7f : 2.5f, k =>
            {
                if (rise) island.Sink = Depth * (1f - k) * (1f - k); // fast out of the deep, settling at the top
                Vector3 p = Vector3.Lerp(far, close, Ease(k)) + (rise ? UnityEngine.Random.insideUnitSphere * 0.35f * (1f - k) : Vector3.zero);
                Aim(p, Quaternion.LookRotation(look - p));
                if (rise && k < 0.9f && Time.time >= nextSplash) { nextSplash = Time.time + 0.09f; Surge(c, k); }
                if (rise && k < 0.85f && Time.time >= nextRumble) { nextRumble = Time.time + 0.8f; try { AudioManager.PlayRandomGlobalClip("WhaleLanding_0", 1, 3, false, 0.8f); } catch { } }
                if (AutoTest.Running && shots < 2 && k > 0.3f + 0.4f * shots) FortressPlugin.Shot("reveal_rising" + ++shots);
            });
            island.Sink = 0f;

            // The lighthouse lights up; the title comes up while the camera drifts round.
            Vector3 lantern = island.LighthousePos + Vector3.up * Siege.LanternHeight;
            if (!_skip) { LightUp(lantern); Sting("QuestFinished", 1f); }
            Style(0);
            Title(L10n.T("revealTitle"), L10n.T("revealSub"), L10n.T("revealLine"));
            Vector3 from = close - c;
            yield return Over(4.6f, k =>
            {
                Vector3 p = c + Quaternion.Euler(0f, -14f * k, 0f) * from + Vector3.up * 2f * k;
                Aim(p, Quaternion.LookRotation(Vector3.Lerp(look, lantern, 0.35f) - p));
                ShowCard(k * 4.6f);
                if (AutoTest.Running && k > 0.6f && !_cardShot) { _cardShot = true; FortressPlugin.Shot("reveal_title"); }
            });
            _cardShot = false;
            yield return Finish(me, L10n.T("islandUnlocked"));
            AutoTest.Count("reveal_done");
        }
        private bool _cardShot;

        // Things lying on the island stay out of sight while it's under the sea, and on the host (whose physics they follow)
        // they hang where they are instead of dropping through the sea floor, until it's back (Finish).
        private void Freeze(FortressIsland island)
        {
            bool host = FishNet.InstanceFinder.IsServerStarted;
            foreach (var i in FindObjectsOfType<Item>())
            {
                if (!i || !i.IsSpawned || i.Holder || !island.OnIsland(i.transform.position)) continue;
                foreach (var r in i.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; _unseen.Add(r); }
                if (host && i.Rig && !i.Rig.isKinematic) { i.Rig.isKinematic = true; _frozen.Add(i.Rig); }
            }
            if (_frozen.Count > 0) BuildSystem.Log.LogInfo($"Reveal: {_frozen.Count} things on the island held in place while it rises.");
        }

        // Spray thrown up round the shore as the island breaks the surface.
        private static void Surge(Vector3 c, float k)
        {
            for (int i = 0; i < 2; i++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f), r = UnityEngine.Random.Range(27f, 36f);
                Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = WaterManager.GetWaterHeight(p);
                WeaponFx.Emit(Spray, p, 2, Vector3.up + new Vector3(p.x - c.x, 0f, p.z - c.z).normalized * 0.4f);
                WeaponFx.Game(UnityEngine.Random.value < 0.5f ? "WaterExplosionWhite" : "WaterExplosion", p, Vector3.up);
            }
        }

        private static ParticleSystem Spray => WeaponFx.System("RevealSpray", () => new WeaponFx.Spec
        {
            Mat = WeaponFx.Splat("Foam", new Color(0.95f, 0.98f, 1f, 1f), "SplashParticle"), Life = new Vector2(1.3f, 2.3f), Speed = new Vector2(7f, 14f),
            Size = new Vector2(1.4f, 3f), Gravity = 1f, Color = WeaponFx.Fade(Color.white, Color.white, 0.6f), SizeOverLife = WeaponFx.Curve(0f, 0.5f, 1f, 1.4f),
            Spin = new Vector2(-0.5f, 0.5f), Shape = ParticleSystemShapeType.Cone, Angle = 18f, Radius = 2f, Max = 1500,
        });

        // ---------- wave 100 ----------

        // Round the lighthouse through the fireworks; the results wait until it's over (FortressIsland).
        public void Victory(Vector3 lighthouseFoot)
        {
            var me = Player.LocalPlayer;
            if (!Playing && me && !me.Dying.IsDead) StartCoroutine(Circle(me, lighthouseFoot));
        }

        private IEnumerator Circle(Player me, Vector3 foot)
        {
            if (!Begin(me)) yield break;
            AutoTest.Count("victory_camera");
            Vector3 eye = _cam.transform.position, lantern = foot + Vector3.up * Siege.LanternHeight;
            Quaternion eyeRot = _cam.transform.rotation;
            Vector3 d = eye - foot;
            float a0 = Mathf.Atan2(d.z, d.x);
            Vector3 At(float a, float r, float h) => foot + new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
            LightUp(lantern);
            yield return Over(1.8f, k =>
            {
                Vector3 p = Vector3.Lerp(eye, At(a0, 17f, 8f), Ease(k));
                Aim(p, Quaternion.Slerp(eyeRot, Quaternion.LookRotation(lantern - p), Ease(k)));
            });
            string team = string.Join("、", PlayerManager.Players.Where(p => p).Select(Loot.NameOf));
            int wins = Loot.Instance && Loot.Instance.ClientVault != null ? Loot.Instance.ClientVault.Wins.Count : 0;
            string count = wins <= 1 ? L10n.T("resultVictoryFirst") : L10n.T("resultVictoryRepeat", wins);
            Style(0);
            Title(L10n.T("revealTitle"), L10n.T("victorySub"), count + "　·　" + team + "　·　" + DateTime.Now.ToString("yyyy-MM-dd"));
            float nextBurst = 0f;
            bool shot = false;
            yield return Over(7.5f, k =>
            {
                Vector3 p = At(a0 + k * Mathf.PI * 0.85f, 17f - 4f * k, 8f + 3f * k);
                Aim(p, Quaternion.LookRotation(lantern - p));
                ShowCard(k * 7.5f - 0.6f);
                if (Time.time >= nextBurst) { nextBurst = Time.time + 0.35f; Burst(lantern); }
                if (AutoTest.Running && !shot && k > 0.45f) { shot = true; FortressPlugin.Shot("victory_circle"); }
            });
            yield return Finish(me, null);
        }

        // A firework over the lighthouse: a ball of glowing chips in one colour, and the game's confetti.
        private static readonly string[] Colours = { "Gold", "Pink", "Cyan", "Green", "Orange" };
        private static void Burst(Vector3 lantern)
        {
            Vector3 p = lantern + new Vector3(UnityEngine.Random.Range(-9f, 9f), UnityEngine.Random.Range(5f, 12f), UnityEngine.Random.Range(-9f, 9f));
            var sparks = WeaponFx.Sparks(Colours[UnityEngine.Random.Range(0, Colours.Length)]);
            for (int i = 0; i < 6; i++) WeaponFx.Emit(sparks, p, 14, UnityEngine.Random.onUnitSphere, size: 0.22f, life: 1.1f);
            WeaponFx.Game(UnityEngine.Random.value < 0.5f ? "Confetti" : "RedConfetti", p, Vector3.up);
            Fx.Sound("ItemImpactHeavy_V" + UnityEngine.Random.Range(1, 4), p, 0.5f, AudioDistance.Long);
        }

        // ---------- a boss's entrance (every 10th wave) ----------

        // When in its entrance a boss turns up (the host spawns it then and holds it still: BossHold) and when it's over (the
        // host lets it go, the rest of the wave comes). Grander every ten waves: 6.5 s at wave 10, 10.5 s at wave 100.
        private static int Tier(int wave) => Mathf.Clamp(wave / 10, 1, 10);
        public static float BossSpawnAt(int wave) => 1.4f + 0.15f * Tier(wave);
        public static float BossSeconds(int wave) => BossSpawnAt(wave) + 4.6f + 0.3f * Tier(wave);

        public void Boss(EnemyKind kind, Vector3 at, Vector3 center, int wave) => StartCoroutine(BossEntrance(kind, at, center, wave));

        // Something is coming: the place stirs and ゴゴゴ fills the screen's edges (a wide shot over the fort). It comes up
        // (a low shot looking up at it), roars its own roar with its own move in a close-up of its face (the speed lines
        // burst, the screen flashes its colour, its name slams in, JoJo style), then a slow, tilted hero shot round it while
        // its card stays up, and last the camera pulls back over the lighthouse to show it against the fort. The camera is
        // the player's if they're free (alive, no menu); else just the screen's part. Space or Esc skips.
        private IEnumerator BossEntrance(EnemyKind kind, Vector3 at, Vector3 center, int wave)
        {
            int tier = Tier(wave);
            float total = BossSeconds(wave), arrive = BossSpawnAt(wave), roarAt = arrive + 1.0f, heroAt = roarAt + 1.4f, outAt = total - 1.1f;
            var me = Player.LocalPlayer;
            bool cam = !Playing && me && !me.Dying.IsDead && !Hud.ModalOpen && !AutoTest.SkipBossCamera && Begin(me);
            if (!cam && Playing) yield break; // the island's entrance or a win is on
            if (!cam) Overlay();
            AutoTest.Count("boss_intro_" + kind);
            BossCard(kind, wave, tier);
            if (tier >= 6) StartCoroutine(Storm(total, tier));
            StartCoroutine(BossStir(kind, at, tier, arrive));
            Vector3 toward = (center - at).Flat();
            toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toward);
            float d0 = 15f + 1.2f * tier;
            Vector3 far = at + toward * (d0 + 14f) + side * d0 * 0.5f + Vector3.up * (7f + 0.5f * tier),
                    near = at + toward * (d0 + 4f) + side * d0 * 0.35f + Vector3.up * (4f + 0.4f * tier);
            Transform boss = null;
            bool arrived = false, roared = false;
            int shotNo = -1;
            float shotAt = 0f, shake = 0f, heroYaw = (wave / 10 % 2 == 0 ? 1f : -1f) * 38f, shotTurn = 0f;
            bool pickTurn = false;
            Vector3 cutFrom = Vector3.zero;
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                if (_skip || (cam && !Fine())) break;
                if (!arrived && t >= arrive) { arrived = true; BossArrival(kind, at); }
                if (arrived && !boss && Enemies.Instance) boss = Enemies.Instance.ClientFind(kind);
                Bounds b = boss ? BoundsOf(boss) : new Bounds(at + Vector3.up * 1.5f, Vector3.one * 3f);
                if (!roared && t >= roarAt) { roared = true; shake = 0.35f + 0.07f * tier; BossRoar(kind, boss, b, tier); }
                Menace(t < outAt ? Mathf.Clamp01(t / (arrive * 0.6f)) : 1f - (t - outAt) / 1.1f, t);
                Lines(t - roarAt);
                CardSlam(t - roarAt);
                if (AutoTest.Running && t >= roarAt + 0.35f && t - Time.deltaTime < roarAt + 0.35f) FortressPlugin.Shot("boss_intro_" + kind);
                if (AutoTest.Running && t >= heroAt + 1f && t - Time.deltaTime < heroAt + 1f) FortressPlugin.Shot("boss_hero_" + kind);
                if (!cam) { yield return null; continue; }
                int shot = t < arrive ? 0 : t < roarAt ? 1 : t < heroAt ? 2 : t < outAt ? 3 : 4;
                if (shot != shotNo) { shotNo = shot; shotAt = t; cutFrom = _cam.transform.position; pickTurn = shot < 4; }
                float s = t - shotAt, R = Mathf.Max(2.5f, b.extents.magnitude);
                Vector3 front = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(toward.x), 0f, Mathf.Abs(toward.z)));
                Vector3 head = b.center + toward * front.magnitude * 0.6f + Vector3.up * b.extents.y * 0.35f;
                Vector3 p, look;
                float fov = 50f, roll = 0f;
                switch (shot)
                {
                    case 0: // the omen: wide over the fort at the spot, pushing in
                        p = Vector3.Lerp(far, near, Ease(Mathf.Clamp01(s / arrive)));
                        look = at + Vector3.up * 1.5f;
                        fov = 55f;
                        break;
                    case 1: // it comes: low by it, looking up as it rises
                        p = Ground(b.center + toward * R * 1.5f + side * R * 0.9f) + Vector3.up * 0.7f + toward * s * 0.6f;
                        look = Vector3.Lerp(new Vector3(b.center.x, b.min.y, b.center.z), b.center + Vector3.up * b.extents.y * 0.5f, Ease(Mathf.Clamp01(s / (roarAt - arrive))));
                        fov = 62f;
                        break;
                    case 2: // the roar: its face, close, a zoom punch
                        p = head + toward * R * 1.15f - side * R * 0.18f + Vector3.up * R * 0.05f;
                        look = head;
                        fov = Mathf.Lerp(38f, 50f, Ease(Mathf.Clamp01(s / 0.25f)));
                        break;
                    case 3: // the hero shot: low, three-quarters, turning slowly round it, tilted
                        p = b.center + Quaternion.Euler(0f, heroYaw + s * 9f * Mathf.Sign(heroYaw), 0f) * (toward * R * 2.3f) + Vector3.up * (0.9f - b.extents.y * 0.25f);
                        look = b.center + Vector3.up * b.extents.y * 0.3f;
                        fov = 52f;
                        roll = 7f * Mathf.Sign(heroYaw);
                        break;
                    default: // out: up and back over the lighthouse, the boss against the fort
                        Vector3 over = center - toward * 9f + Vector3.up * (12f + 0.6f * tier + R * 0.5f);
                        p = Vector3.Lerp(cutFrom, over, Ease(Mathf.Clamp01(s / 1.1f)));
                        look = Vector3.Lerp(b.center, Vector3.Lerp(b.center, center, 0.35f), Ease(Mathf.Clamp01(s / 1.1f)));
                        fov = 55f;
                        break;
                }
                // A shot whose camera would be behind a rock (a boss in the rock arch, under a cliff) is turned round it,
                // the least it takes to see it, for the whole shot.
                if (pickTurn) { pickTurn = false; shotTurn = OpenTurn(look, p); }
                if (shotTurn != 0f) p = look + Quaternion.Euler(0f, shotTurn, 0f) * (p - look);
                p = Clear(look, p);
                shake *= Mathf.Exp(-3.5f * Time.deltaTime);
                p += UnityEngine.Random.insideUnitSphere * shake;
                Aim(p, Quaternion.LookRotation(look - p) * Quaternion.Euler(0f, 0f, roll));
                _cam.fieldOfView = fov;
                yield return null;
            }
            Menace(0f, 0f);
            Lines(-1f);
            if (cam) yield return Finish(me, null);
            else
            {
                for (float t = 0f; t < 0.4f; t += Time.deltaTime) { if (_bossCard) _bossCard.alpha = 1f - t / 0.4f; yield return null; }
                if (_canvas) _canvas.gameObject.SetActive(false);
            }
            if (_bossCard) _bossCard.alpha = 0f;
        }

        // The ground (or the sea) under `p`.
        private static Vector3 Ground(Vector3 p)
        {
            float y = WaterManager.GetWaterHeight(p);
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var h, 60f, GameInfo.LevelLayer)) y = Mathf.Max(y, h.point.y);
            return new Vector3(p.x, y, p.z);
        }

        // The least turn round `look` that gives a camera at `eye` a clear view of it (none: no turn).
        private static float OpenTurn(Vector3 look, Vector3 eye)
        {
            foreach (float yaw in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f })
            {
                Vector3 e = look + Quaternion.Euler(0f, yaw, 0f) * (eye - look);
                e.y = Mathf.Max(e.y, Ground(e).y + 0.4f);
                if (!Physics.Linecast(look, e, GameInfo.LevelLayer)) return yaw;
            }
            return 0f;
        }

        // Keep the camera out of the rocks and the sea: pulled in in front of anything between it and what it looks at.
        private static Vector3 Clear(Vector3 look, Vector3 eye)
        {
            if (Physics.Linecast(look, eye, out var h, GameInfo.LevelLayer)) eye = h.point + (look - eye).normalized * 0.5f + Vector3.up * 0.3f;
            float floor = Ground(eye).y + 0.4f;
            if (eye.y < floor) eye.y = floor;
            return eye;
        }

        private static Bounds BoundsOf(Transform t) => BossLooks.WorldBounds(t);

        // Its colour: the flash, and the speed lines' tint.
        private static Color ColourOf(EnemyKind k) => BossLooks.Colour(k);

        // Before it comes: the place stirs, more and more (its own entrance, BossBrains, brings it in).
        private IEnumerator BossStir(EnemyKind kind, Vector3 at, int tier, float until)
        {
            float water = WaterManager.GetWaterHeight(at);
            Vector3 sea = new Vector3(at.x, water, at.z);
            var body = EnemyCatalog.BodyOf(kind);
            for (float t = 0f; t < until; t += 0.15f)
            {
                float k = t / until;
                switch (body)
                {
                    case EnemyCatalog.Body.Land:
                        WeaponFx.Emit(WeaponFx.Dust, at + UnityEngine.Random.insideUnitSphere.Flat() * (1f + 3f * k), 1 + (int)(4 * k), Vector3.up);
                        break;
                    case EnemyCatalog.Body.Sea:
                        Ring(sea, 1.5f + 6f * k, 10, WeaponFx.Foam, 1);
                        WeaponFx.Emit(WeaponFx.Bubbles, sea + UnityEngine.Random.insideUnitSphere.Flat() * 4f, 5, Vector3.up);
                        break;
                    default:
                        if (kind == EnemyKind.Koi || kind == EnemyKind.Angler) { Ring(sea, 1.5f + 5f * k, 8, WeaponFx.Foam, 1); WeaponFx.Emit(WeaponFx.Bubbles, sea + UnityEngine.Random.insideUnitSphere.Flat() * 3f, 3, Vector3.up); }
                        else if (UnityEngine.Random.value < 0.3f) WeaponFx.Emit(BossVfx.Sparks(ColourOf(kind)), at + UnityEngine.Random.insideUnitSphere * 6f, 2, Vector3.down, size: 0.12f);
                        if (tier >= 7 && UnityEngine.Random.value < 0.12f) StartCoroutine(Lightning(at + UnityEngine.Random.insideUnitSphere.Flat() * 18f - Vector3.up * 14f));
                        break;
                }
                yield return new WaitForSeconds(0.15f);
            }
        }

        // It comes: a puff where it comes up (not a blast: the show is the boss).
        private static void BossArrival(EnemyKind kind, Vector3 at)
        {
            Vector3 sea = new Vector3(at.x, WaterManager.GetWaterHeight(at), at.z);
            switch (EnemyCatalog.BodyOf(kind))
            {
                case EnemyCatalog.Body.Land: Ring(at, 1.5f, 10, WeaponFx.Dust, 2); break;
                case EnemyCatalog.Body.Sea: WeaponFx.Game("WaterSplashWhite", sea, Vector3.up); Ring(sea, 2.5f, 12, WeaponFx.Foam, 2); break;
            }
        }

        // The roar: its own voice and its own move (what its skills look like), a shockwave round it.
        private void BossRoar(EnemyKind kind, Transform boss, Bounds b, int tier)
        {
            Vector3 c = b.center, top = c + Vector3.up * b.extents.y, foot = new Vector3(c.x, b.min.y, c.z);
            Vector3 sea = new Vector3(c.x, WaterManager.GetWaterHeight(c), c.z);
            Vector3 ground = new Vector3(c.x, BossFx.Ground(c), c.z);
            int id = boss && boss.GetComponent<FishNet.Object.NetworkObject>() is FishNet.Object.NetworkObject nob ? nob.ObjectId : 0;
            void Voice(string clip, float vol = 1f) { try { AudioManager.PlayGlobalClip(clip, false, vol); } catch { } Fx.Sound(clip, c, vol, AudioDistance.Long); }
            void Vfx(string name, Vector3 at) { try { VFXManager.Play(name, at); } catch { } }
            void Move(string key, Vector3 a, Vector3 bb, int arg = 0, int who = 0) { if (BossFx.Instance) BossFx.Instance.Play(new FxEvent { Kind = FxKind.BossFx, Key = key, From = a, To = bb, PieceId = id, Arg = arg, Who = who }); }
            StartCoroutine(Shockwave(ground, 3f + 0.6f * b.extents.magnitude, kind));
            switch (kind)
            {
                case EnemyKind.Warden: // claws clacked overhead, sparks off them
                    StartCoroutine(Repeat(3, 0.16f, () => { Voice("SpiderCrabSnip_V" + UnityEngine.Random.Range(1, 4)); BossVfx.Burst(top, ColourOf(kind), 25, 0.12f); }));
                    Voice("MetallicItemImpact_V2");
                    Vfx("Angry", top);
                    break;
                case EnemyKind.Urchin: // every spike out at once
                    Voice("Pufferfish");
                    Move("urchin.bristle", c, c, 900);
                    BossVfx.Burst(c, ColourOf(kind), 80, 0.16f);
                    Vfx("Angry", top);
                    break;
                case EnemyKind.Angler: // the lantern flares in the dark
                    Voice("WhaleNoise_02");
                    Move("angler.lure", c, c, 900);
                    StartCoroutine(After(0.5f, () => Move("angler.flash", top, c)));
                    break;
                case EnemyKind.Steed: // up on its hind legs, a crown of gold
                    Voice("QuestFinished");
                    Voice("BoatLandCollisionMedium_V2");
                    BossVfx.Burst(top, ColourOf(kind), 70, 0.16f);
                    StartCoroutine(Lightning(c + side() * 5f));
                    break;
                case EnemyKind.Stonefish: // the ground answers: spires all round, poison on the air
                    Voice("LavaEruption_01");
                    StartCoroutine(Spires(foot, b.extents.magnitude + 2f, 10, 2.2f, new Color(0.4f, 0.9f, 0.3f)));
                    Ring(ground + Vector3.up * 0.5f, b.extents.magnitude + 1f, 16, WeaponFx.Gas, 2);
                    break;
                case EnemyKind.Skyfish: // a sonic boom
                    Voice("IntroCrash");
                    Voice("PunchSwoosh");
                    WeaponFx.Emit(WeaponFx.Rings, c, 1, side(), color: Color.white, size: 14f);
                    StartCoroutine(Lightning(c + side() * 7f - Vector3.up * 6f));
                    break;
                case EnemyKind.Kraken: // tentacles up out of the sea round it
                    Voice("LavaWhaleNoise_02");
                    Move("kraken.tentacles", sea - (Center(c) - sea).Flat().normalized * 2f, Center(c), 1800, 4);
                    Vfx("WaterExplosion", sea);
                    break;
                case EnemyKind.Voxel: // it glitches
                    Voice("Error");
                    Move("voxel.fatal", c, c, 900);
                    Move("voxel.in", c, c);
                    break;
                case EnemyKind.Koi: // a crash of water, a ring of gold
                    Voice("WhaleNoise_05");
                    Vfx("WaterExplosion", sea);
                    WeaponFx.Game("WaterExplosionWhite", sea, Vector3.up);
                    BossVfx.Burst(c, ColourOf(kind), 60, 0.15f);
                    break;
                default: // the Eye: it opens, the sky bleeds
                    Voice("LavaWhaleNoise_04");
                    StartCoroutine(After(0.5f, () => Voice("LavaWhaleDeathCry_02", 0.9f)));
                    Move("eye.open", c, c, 1800);
                    for (int i = 0; i < 4; i++) StartCoroutine(Lightning(c + Quaternion.Euler(0f, 90f * i, 0f) * Vector3.forward * 9f - Vector3.up * 10f));
                    Sting("BossFightIntro", 1f);
                    break;
            }
            Flash(ColourOf(kind));
            Vector3 side() => _cam ? _cam.transform.right : Vector3.right;
        }
        private static Vector3 Center(Vector3 near) => FortressIsland.Instance ? FortressIsland.Instance.Center : near;

        private IEnumerator Repeat(int n, float every, Action a) { for (int i = 0; i < n; i++) { a(); yield return new WaitForSeconds(every); } }
        private IEnumerator After(float seconds, Action a) { yield return new WaitForSeconds(seconds); a(); }

        // A ring of dust (or water) rolling out from where it stands.
        private IEnumerator Shockwave(Vector3 at, float reach, EnemyKind kind)
        {
            bool wet = at.y <= WaterManager.GetWaterHeight(at) + 0.6f;
            for (float t = 0f; t < 0.6f; t += 0.06f)
            {
                Ring(at, 1f + reach * (t / 0.6f) * 2.2f, 16, wet ? WeaponFx.Foam : WeaponFx.Dust, 1);
                yield return new WaitForSeconds(0.06f);
            }
        }

        // Particles in a ring round `at` (n spots, `each` per spot).
        private static void Ring(Vector3 at, float radius, int n, ParticleSystem ps, int each)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (i + UnityEngine.Random.value * 0.5f) * Mathf.PI * 2f / n;
                WeaponFx.Emit(ps, at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, each, Vector3.up);
            }
        }

        // A tidal ring rolling out across the sea.
        private IEnumerator TideRing(Vector3 at, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.08f)
            {
                Ring(new Vector3(at.x, WaterManager.GetWaterHeight(at), at.z), 3f + 40f * (t / seconds), 28, Spray, 1);
                yield return new WaitForSeconds(0.08f);
            }
        }

        // A bolt from the sky to `at`: a jagged glowing line, a flash, thunder.
        private IEnumerator Lightning(Vector3 at)
        {
            Vector3 top = at + Vector3.up * 45f + UnityEngine.Random.insideUnitSphere.Flat() * 6f;
            var line = WeaponFx.Line("Lightning", WeaponFx.Emissive("Lightning", new Color(0.75f, 0.85f, 1f), 9f), 0.35f);
            line.positionCount = 9;
            for (int i = 0; i < 9; i++) line.SetPosition(i, Vector3.Lerp(top, at, i / 8f) + (i > 0 && i < 8 ? UnityEngine.Random.insideUnitSphere * 2.2f : Vector3.zero));
            var flash = new GameObject("LightningFlash").AddComponent<Light>();
            flash.type = LightType.Point; flash.color = new Color(0.8f, 0.88f, 1f); flash.range = 60f; flash.intensity = 6f; flash.transform.position = at + Vector3.up * 8f;
            WeaponFx.Emit(WeaponFx.Sparks("Cyan"), at, 20, Vector3.up, size: 0.1f);
            Fx.Sound("Explosion", at, 0.5f, AudioDistance.Long);
            yield return new WaitForSeconds(0.08f);
            if (line) line.enabled = false;
            yield return new WaitForSeconds(0.05f);
            if (line) line.enabled = true;
            yield return new WaitForSeconds(0.1f);
            if (line) Destroy(line.gameObject);
            if (flash) Destroy(flash.gameObject);
        }

        // Glowing coral rising out of the ground in a ring, then sinking back.
        private IEnumerator Spires(Vector3 at, float radius, int n, float seconds, Color c)
        {
            var mat = BossVfx.Glow(c, 4f);
            var spires = new List<Transform>();
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var s = WeaponFx.Piece(WeaponFx.Cyl, mat);
                s.position = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                s.rotation = Quaternion.Euler(UnityEngine.Random.Range(-12f, 12f), 0f, UnityEngine.Random.Range(-12f, 12f));
                spires.Add(s);
            }
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds, h = k < 0.75f ? Mathf.SmoothStep(0f, 1f, k / 0.75f) : 1f - (k - 0.75f) / 0.25f;
                for (int i = 0; i < spires.Count; i++) if (spires[i]) spires[i].localScale = new Vector3(0.45f, 3.2f * h * (0.7f + 0.3f * Mathf.Sin(i * 1.7f)), 0.45f);
                yield return null;
            }
            foreach (var s in spires) if (s) Destroy(s.gameObject);
        }

        // From wave 60: the sky goes dark (blood red at 100) while it comes, then clears.
        private IEnumerator Storm(float seconds, int tier)
        {
            float d0 = RenderSettings.fogDensity;
            Color c0 = RenderSettings.fogColor;
            Color to = tier >= 10 ? new Color(0.35f, 0.04f, 0.04f) : new Color(0.16f, 0.18f, 0.24f);
            float dTo = d0 * (tier >= 10 ? 3.2f : 2.2f);
            for (float t = 0f; t < seconds + 1.5f; t += Time.deltaTime)
            {
                float k = t < seconds * 0.5f ? t / (seconds * 0.5f) : t < seconds ? 1f : 1f - (t - seconds) / 1.5f;
                RenderSettings.fogDensity = Mathf.Lerp(d0, dTo, k);
                RenderSettings.fogColor = Color.Lerp(c0, to, k);
                yield return null;
            }
            RenderSettings.fogDensity = d0;
            RenderSettings.fogColor = c0;
        }

        // ---------- the lighthouse lighting up ----------

        private void LightUp(Vector3 lantern)
        {
            var go = new GameObject("RevealLight");
            go.transform.position = lantern;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.88f, 0.62f); l.range = 45f; l.intensity = 0f; l.shadows = LightShadows.None;
            _props.Add(go);
            var beams = new GameObject("RevealBeams").transform;
            beams.position = lantern;
            _props.Add(beams.gameObject);
            var mat = WeaponFx.Sprite("RevealBeam", Texture2D.whiteTexture, new Color(1f, 0.93f, 0.7f, 1f));
            foreach (float yaw in new[] { 0f, 180f })
            {
                var b = new GameObject("Beam");
                b.transform.SetParent(beams, false);
                b.transform.localRotation = Quaternion.Euler(4f, yaw, 0f);
                b.transform.localScale = new Vector3(3.2f, 3.2f, 42f);
                b.AddComponent<MeshFilter>().sharedMesh = Cone;
                var r = b.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var flash = WeaponFx.Piece(WeaponFx.Ball, WeaponFx.Emissive("RevealFlash", new Color(1f, 0.9f, 0.6f), 6f));
            flash.position = lantern;
            _props.Add(flash.gameObject);
            StartCoroutine(Lit(l, beams, flash));
        }

        private IEnumerator Lit(Light l, Transform beams, Transform flash)
        {
            for (float t = 0f; l && beams; t += Time.deltaTime)
            {
                l.intensity = t < 0.25f ? Mathf.Lerp(0f, 9f, t / 0.25f) : Mathf.Lerp(9f, 4f, Mathf.Clamp01((t - 0.25f) / 1.2f));
                beams.rotation = Quaternion.Euler(0f, t * 75f, 0f);
                if (flash) { float s = t < 0.2f ? Mathf.Lerp(0.3f, 5f, t / 0.2f) : Mathf.Lerp(5f, 0f, (t - 0.2f) / 0.5f); flash.localScale = Vector3.one * Mathf.Max(0f, s); if (t > 0.7f) Destroy(flash.gameObject); }
                yield return null;
            }
        }

        // An open cone along +z, tip at the origin, radius 1 one unit out; bright at the tip, gone at the rim.
        private static Mesh _cone;
        private static Mesh Cone
        {
            get
            {
                if (_cone) return _cone;
                const int n = 18;
                var v = new List<Vector3> { Vector3.zero };
                var col = new List<Color> { new Color(1f, 1f, 1f, 0.5f) };
                var tris = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f));
                    col.Add(new Color(1f, 1f, 1f, 0f));
                    tris.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % n });
                }
                _cone = new Mesh { name = "RevealCone" };
                _cone.SetVertices(v);
                _cone.SetColors(col);
                _cone.SetTriangles(tris, 0);
                _cone.RecalculateNormals();
                _cone.RecalculateBounds();
                return _cone;
            }
        }

        // ---------- the camera ----------

        private bool Begin(Player me)
        {
            if (Playing || !me || !me.Camera || !me.DeathCam || me.Dying.IsDead || !Hud.Font) return false;
            _cam = AccessTools.Field(typeof(PlayerDeathCam), "_deathCam")?.GetValue(me.DeathCam) as Camera;
            if (!_cam) return false;
            var eye = me.Camera.CamTransform;
            _fov = me.Camera.Cam.fieldOfView;
            _camFov = _cam.fieldOfView;
            _cam.transform.SetPositionAndRotation(eye.position, eye.rotation);
            _cam.fieldOfView = _fov;
            _cam.gameObject.SetActive(true);
            me.SetCurCam(_cam);
            me.Camera.Cam.enabled = false;
            Playing = true;
            _skip = false; _held = 0f;
            HideHud(true);
            Fog(true);
            Overlay();
            StartCoroutine(Bars(true));
            return true;
        }

        private void Aim(Vector3 pos, Quaternion rot) { if (_cam) _cam.transform.SetPositionAndRotation(pos, rot); }

        // Fade to black, hand the player back their own camera, fade in on them.
        private IEnumerator Finish(Player me, string banner)
        {
            yield return Fade(0f, 1f, 0.45f);
            if (FortressIsland.Instance) FortressIsland.Instance.Sink = 0f;
            foreach (var rb in _frozen) if (rb) rb.isKinematic = false;
            foreach (var r in _unseen) if (r) r.enabled = true;
            _frozen.Clear();
            _unseen.Clear();
            Fog(false);
            foreach (var p in _props) if (p) Destroy(p);
            _props.Clear();
            if (_card) _card.alpha = 0f;
            if (me && me.Camera)
            {
                me.Camera.Cam.enabled = true;
                if (!me.Dying.IsDead) { me.SetCurCam(me.Camera.Cam); if (_cam) _cam.gameObject.SetActive(false); }
            }
            if (_cam) _cam.fieldOfView = _camFov;
            HideHud(false);
            Playing = false;
            StartCoroutine(Bars(false));
            yield return Fade(1f, 0f, 0.6f);
            if (_canvas) _canvas.gameObject.SetActive(false);
            if (banner != null) Hud.Banner(banner, 6f);
        }

        // Runs `step(0..1)` each frame for `seconds`; a skip (or the player dying, the island going) cuts it short.
        private IEnumerator Over(float seconds, Action<float> step)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (_skip || !Fine()) yield break;
                step(t / seconds);
                yield return null;
            }
            if (!_skip && Fine()) step(1f);
        }

        private bool Fine()
        {
            var me = Player.LocalPlayer;
            return me && !me.Dying.IsDead && _cam && _cam.gameObject.activeSelf;
        }

        private static float Ease(float k) => k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;

        private static void Sting(string clip, float volume)
        {
            try { AudioManager.PlayGlobalClip(clip, false, volume); } catch { }
        }

        // Every screen canvas but ours (the game's HUD and chat, our HUD) is switched off while the camera is away. Only the
        // Canvas component: what's under it stays alive (a label made meanwhile still sets up its font).
        private void HideHud(bool hide)
        {
            if (hide)
            {
                _hidden.Clear();
                foreach (var c in FindObjectsOfType<Canvas>())
                    if (c.isRootCanvas && c.enabled && c.renderMode != RenderMode.WorldSpace && c != _canvas) { c.enabled = false; _hidden.Add(c); }
            }
            else
            {
                foreach (var c in _hidden) if (c) c.enabled = true;
                _hidden.Clear();
            }
        }

        // Thinner sea haze while the camera is far out (the game sets the density only when it changes the weather).
        private void Fog(bool thin)
        {
            if (thin) { _fog = RenderSettings.fogDensity; RenderSettings.fogDensity = _fog * 0.35f; }
            else if (_fog >= 0f) { if (Mathf.Approximately(RenderSettings.fogDensity, _fog * 0.35f)) RenderSettings.fogDensity = _fog; _fog = -1f; }
        }

        // ---------- the screen: black bars, the fade, the title card ----------

        private void Overlay()
        {
            if (!_canvas)
            {
                Hud.Canvas("Over99Reveal", 900, ref _canvas);
                var root = _canvas.transform;
                var card = Hud.Node(root, "Card", new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1400f, 420f));
                _card = card.gameObject.AddComponent<CanvasGroup>();
                var band = Hud.Node(card, "Band", new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(2400f, 380f));
                var shade = band.gameObject.AddComponent<RawImage>();
                shade.texture = Shade;
                shade.raycastTarget = false;
                _title = Hud.Label(card, "", 172f, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(1400f, 210f), TextAlignmentOptions.Center, Color.white, bold: true);
                _title.enableVertexGradient = true;
                _title.colorGradient = new VertexGradient(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.95f, 0.7f), new Color(1f, 0.62f, 0.18f), new Color(1f, 0.62f, 0.18f));
                _title.characterSpacing = 6f;
                _title.outlineWidth = 0.14f;
                _title.outlineColor = new Color32(70, 34, 6, 255);
                Hud.Box(card, new Vector2(0.5f, 0.5f), new Vector2(0f, -38f), new Vector2(460f, 5f), Hud.Gold, 2.5f);
                _sub = Hud.Label(card, "", 50f, new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(1400f, 64f), TextAlignmentOptions.Center, bold: true, outline: true);
                _line = Hud.Label(card, "", 33f, new Vector2(0.5f, 0.5f), new Vector2(0f, -148f), new Vector2(1400f, 48f), TextAlignmentOptions.Center, Color.white, outline: true);
                BuildJoJo(root);
                _barTop = Strip(root, true); // over the flash and the speed lines
                _barBottom = Strip(root, false);
                var hint = Hud.Label(root, Hud.Keys(L10n.T("revealSkip")), 22f, new Vector2(1f, 0f), new Vector2(-40f, 44f), new Vector2(400f, 40f), TextAlignmentOptions.Right, Hud.Muted);
                hint.rectTransform.pivot = new Vector2(1f, 0.5f);
                _skipTrack = Hud.Box(root, new Vector2(1f, 0f), new Vector2(-40f, 18f), new Vector2(160f, 6f), new Color(1f, 1f, 1f, 0.25f), 3f, pivot: new Vector2(1f, 0.5f));
                _skipFill = Hud.Box(_skipTrack.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 6f), Hud.Gold, 3f, pivot: new Vector2(0f, 0.5f));
                _skipTrack.gameObject.SetActive(false);
                var fade = Hud.Node(root, "Fade", Vector2.zero, Vector2.zero, Vector2.zero);
                fade.anchorMin = Vector2.zero; fade.anchorMax = Vector2.one;
                _fade = fade.gameObject.AddComponent<Image>();
                _fade.color = Color.clear;
                _fade.raycastTarget = false;
            }
            _canvas.gameObject.SetActive(true);
            _card.alpha = 0f;
            _fade.color = Color.clear;
        }

        // Dark in the middle, clear at the edges: lifts the title off a bright sea.
        private static Texture2D _shade;
        private static Texture2D Shade
        {
            get
            {
                if (_shade) return _shade;
                const int w = 64, h = 32;
                _shade = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RevealShade" };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = Mathf.Abs(x / (w - 1f) * 2f - 1f), v = Mathf.Abs(y / (h - 1f) * 2f - 1f);
                        _shade.SetPixel(x, y, new Color(0f, 0f, 0f, 0.42f * (1f - u * u) * (1f - v * v)));
                    }
                _shade.Apply();
                return _shade;
            }
        }

        // Everything is placed from the screen's edges (any shape of window keeps it all on screen): ゴゴゴ down the right
        // edge and the upper left, the name card in the lower left above the black bar.
        private void BuildJoJo(Transform root)
        {
            var mid = new Vector2(0.5f, 0.5f);
            var rng = new System.Random(5);
            float R() => (float)rng.NextDouble();
            // ゴゴゴ (or 轟 where the font has no kana), purple.
            _menaceRoot = Hud.Node(root, "Menace", mid, Vector2.zero, Vector2.zero);
            _menaceRoot.anchorMin = Vector2.zero; _menaceRoot.anchorMax = Vector2.one;
            string go = Hud.Font && Hud.Font.HasCharacter('ゴ', true) ? "ゴ" : "轟";
            for (int i = 0; i < 12; i++)
            {
                bool right = i < 7;
                int k = right ? i : i - 7;
                var anchor = new Vector2(right ? 1f : 0f, 0.5f);
                var pos = right ? new Vector2(-(105f + R() * 80f), -320f + 110f * k + R() * 25f) : new Vector2(105f + R() * 80f, 30f + 85f * k + R() * 20f);
                var g = Hud.Label(_menaceRoot, go, 66f + R() * 56f, anchor, pos, new Vector2(180f, 180f), TextAlignmentOptions.Center, Color.white, bold: true, pivot: mid);
                g.enableVertexGradient = true;
                g.colorGradient = new VertexGradient(new Color(0.9f, 0.4f, 1f), new Color(0.9f, 0.4f, 1f), new Color(0.35f, 0.02f, 0.5f), new Color(0.35f, 0.02f, 0.5f));
                g.outlineWidth = 0.22f;
                g.outlineColor = new Color32(255, 255, 255, 235);
                g.rectTransform.localRotation = Quaternion.Euler(0f, 0f, R() * 30f - 15f);
                g.alpha = 0f;
                _gogo.Add(g);
            }
            // Speed lines: black streaks from round the middle out to the edges.
            _linesRoot = Hud.Node(root, "SpeedLines", mid, Vector2.zero, Vector2.zero);
            for (int i = 0; i < 64; i++)
            {
                var l = Hud.Node(_linesRoot, "Line", mid, Vector2.zero, new Vector2(1000f, 8f), new Vector2(0f, 0.5f));
                l.localRotation = Quaternion.Euler(0f, 0f, i * 360f / 64f + R() * 4f);
                var img = l.gameObject.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.6f);
                img.raycastTarget = false;
                _streaks.Add((l, img));
            }
            _linesRoot.gameObject.SetActive(false);
            var f = Hud.Node(root, "Flash", Vector2.zero, Vector2.zero, Vector2.zero);
            f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one;
            _flashImg = f.gameObject.AddComponent<Image>();
            _flashImg.color = Color.clear;
            _flashImg.raycastTarget = false;
            // The name card, lower left, tilted: the wave, the title, the name, the line. Each shrinks to fit its width.
            var corner = Vector2.zero;
            _bossCardRt = Hud.Node(root, "BossCard", corner, CardAt, new Vector2(980f, 330f));
            _bossCardRt.localRotation = Quaternion.Euler(0f, 0f, 4f);
            _bossCard = _bossCardRt.gameObject.AddComponent<CanvasGroup>();
            _bossCard.alpha = 0f;
            TextMeshProUGUI Line(float size, float min, Vector2 pos, Vector2 box)
            {
                var t = Hud.Label(_bossCardRt, "", size, corner, pos, box, TextAlignmentOptions.BottomLeft, Color.white, bold: true);
                t.fontStyle = FontStyles.Bold | FontStyles.Italic;
                t.enableWordWrapping = false;
                t.enableAutoSizing = true; t.fontSizeMin = min; t.fontSizeMax = size;
                return t;
            }
            _bossWave = Line(28f, 18f, new Vector2(24f, 282f), new Vector2(600f, 40f));
            _bossWave.fontStyle = FontStyles.Bold;
            _bossWave.color = new Color(0.9f, 0.92f, 1f);
            _bossWave.outlineWidth = 0.18f; _bossWave.outlineColor = new Color(0f, 0f, 0f, 0.85f);
            _bossEpithet = Line(44f, 26f, new Vector2(20f, 218f), new Vector2(940f, 62f));
            _bossEpithet.enableVertexGradient = true;
            _bossEpithet.colorGradient = new VertexGradient(new Color(1f, 0.95f, 0.65f), new Color(1f, 0.95f, 0.65f), new Color(1f, 0.6f, 0.15f), new Color(1f, 0.6f, 0.15f));
            _bossEpithet.outlineWidth = 0.2f; _bossEpithet.outlineColor = new Color32(40, 14, 0, 255);
            _bossName = Line(140f, 64f, new Vector2(0f, 62f), new Vector2(980f, 158f));
            _bossName.outlineWidth = 0.24f;
            _bossQuote = Line(30f, 18f, new Vector2(44f, 12f), new Vector2(920f, 48f));
            _bossQuote.outlineWidth = 0.18f; _bossQuote.outlineColor = new Color(0f, 0f, 0f, 0.85f);
        }
        private static readonly Vector2 CardAt = new Vector2(64f, Bar + 26f); // from the screen's lower left, clear of the bar

        private void BossCard(EnemyKind kind, int wave, int tier)
        {
            _bossWave.text = L10n.T("bossIntroWave", wave);
            _bossEpithet.text = "『" + L10n.T("bossEpithet" + kind) + "』";
            _bossName.text = L10n.T("bossName" + kind);
            _bossQuote.text = "「" + L10n.T("bossQuote" + kind) + "」";
            Style(_bossName, tier);
            _bossName.fontSizeMax = Mathf.Min(140f, _bossName.fontSize);
            _bossCard.alpha = 0f;
            _flashImg.color = Color.clear;
            _flashAt = -10f;
        }

        // ゴゴゴ: `level` 0..1, throbbing.
        private void Menace(float level, float t)
        {
            for (int i = 0; i < _gogo.Count; i++)
            {
                var g = _gogo[i];
                g.alpha = level * (0.8f + 0.2f * Mathf.Sin(t * 7f + i));
                g.rectTransform.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(t * 9f + i * 1.7f));
            }
        }

        // The speed lines, `t` seconds after the roar (shown for 0.9 s), trembling.
        private void Lines(float t)
        {
            bool on = t >= 0f && t < 0.9f;
            if (_linesRoot.gameObject.activeSelf != on) _linesRoot.gameObject.SetActive(on);
            if (!on) return;
            float a = 0.65f * (1f - t / 0.9f);
            foreach (var (rt, img) in _streaks)
            {
                float r = 320f + UnityEngine.Random.value * 160f;
                rt.anchoredPosition = (Vector2)(rt.localRotation * Vector3.right) * r;
                rt.sizeDelta = new Vector2(1000f, 3f + UnityEngine.Random.value * 12f);
                img.color = new Color(0f, 0f, 0f, a * (0.6f + 0.4f * UnityEngine.Random.value));
            }
        }

        // A white flash, then the screen washed in the boss's colour, fading.
        private void Flash(Color c) { _flashAt = Time.time; _flashColour = c; }

        // The card slams in at the roar (big, then settling), and the flash fades.
        private void CardSlam(float t)
        {
            float f = Time.time - _flashAt;
            _flashImg.color = f < 0.06f ? new Color(1f, 1f, 1f, 0.85f) : f < 0.6f ? new Color(_flashColour.r, _flashColour.g, _flashColour.b, 0.5f * (1f - (f - 0.06f) / 0.54f)) : Color.clear;
            if (t < 0f) { _bossCard.alpha = 0f; return; }
            _bossCard.alpha = 1f;
            float k = Mathf.Clamp01(t / 0.18f);
            _bossCardRt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, 1f - (1f - k) * (1f - k)); // grows from its lower left corner: stays on screen
            _bossCardRt.anchoredPosition = CardAt + new Vector2(Mathf.Min(t, 6f) * 5f, 0f);
        }

        private static Image Strip(Transform root, bool top)
        {
            var rt = Hud.Node(root, top ? "BarTop" : "BarBottom", Vector2.zero, Vector2.zero, new Vector2(0f, Bar));
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 0f : 1f); // starts off screen; Bars slides it in
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            return img;
        }
        private const float Bar = 120f;

        private IEnumerator Bars(bool show)
        {
            for (float t = 0f; t <= 0.7f; t += Time.deltaTime)
            {
                float k = Ease(Mathf.Clamp01(t / 0.7f)), y = (show ? k : 1f - k) * Bar;
                if (_barTop) _barTop.rectTransform.anchoredPosition = new Vector2(0f, -y);
                if (_barBottom) _barBottom.rectTransform.anchoredPosition = new Vector2(0f, y);
                yield return null;
            }
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (_fade) _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            if (_fade) _fade.color = new Color(0f, 0f, 0f, to);
        }

        // The title's look: 0 the gold OVER 99; a boss's tier (1-10, its wave / 10) runs from silver through gold and
        // orange to blood red, and bigger.
        private void Style(int tier) => Style(_title, tier);
        private static void Style(TMP_Text text, int tier)
        {
            Color top, bottom;
            if (tier == 0) { top = new Color(1f, 0.95f, 0.7f); bottom = new Color(1f, 0.62f, 0.18f); }
            else if (tier <= 3) { top = Color.white; bottom = new Color(0.62f, 0.72f, 0.82f); }
            else if (tier <= 6) { top = new Color(1f, 0.93f, 0.6f); bottom = new Color(0.95f, 0.55f, 0.12f); }
            else if (tier <= 9) { top = new Color(1f, 0.75f, 0.4f); bottom = new Color(0.85f, 0.2f, 0.08f); }
            else { top = new Color(1f, 0.35f, 0.3f); bottom = new Color(0.45f, 0f, 0.02f); }
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(top, top, bottom, bottom);
            text.fontSize = tier == 0 ? 172f : 118f + 6f * tier;
            text.outlineColor = tier >= 10 ? new Color32(30, 0, 0, 255) : new Color32(70, 34, 6, 255);
        }

        private void Title(string title, string sub, string line)
        {
            _title.text = title;
            _sub.text = sub;
            _line.text = line;
        }

        // The card `t` seconds in: the title grows in, then the line under it, then the small print.
        private void ShowCard(float t)
        {
            if (!_card) return;
            _card.alpha = 1f;
            float a = Mathf.Clamp01(t / 0.6f);
            _title.alpha = a;
            _title.transform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, 1f - (1f - a) * (1f - a));
            _sub.alpha = Mathf.Clamp01((t - 0.5f) / 0.5f);
            _line.alpha = Mathf.Clamp01((t - 1.0f) / 0.5f);
        }
    }
}
