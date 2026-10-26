using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using UnityEngine;

namespace HtfFortress
{
    public enum FxKind : byte { Tracer, Cannonball, Blast, Spit, Announce, Harpoon, Mortar, Zap, Burn, Scoop, Sell, Heal, Dust, Wave, Decoy, Snipe, Flame, Proc,
        Beam, Gas, Boomerang, Shark, Bubble, Hook, Whirl, Sonic, WeaponHit, // this row: fortress weapons (drawn by Weapons)
        Victory, Crash,
        Net, Jelly, Water, Puffer, Star, Claw, // the second set of weapons (drawn by Weapons too)
        Absorb, // something swallowed by a chute (Swallow): PieceId the item, Arg how
        PieceLost, // a turret destroyed: Arg its type, Who its level (bottom-left, to everyone)
        DeathMark, // a player died here: what they carried lies here (From); Who their object id
        BossIntro, // a boss's entrance (Reveal.Boss): To where it comes, From the island, PieceId the wave, Arg the kind
        BossFx, // a boss's warnings and moves (BossFx): Key the move, PieceId the boss
        Rocket, // a rocket (drawn by Weapons): Arg its flight in ms
        Mend, // a repair tower's spark to what it mends: PieceId the tower
        Absorber, // the absorber's moves (drawn by BossMoves.PlayAbsorber): Key the move
        Drip, // a wet fish dripping (drawn by Weapons)
        Gatling, // the gatling's bullets (drawn by Weapons): PieceId how many (-1: one glancing on to the next fish), Arg how far it's spun up (%)
        Soul, // souls rising at the lighthouse after a settlement (drawn by Weapons)
        FinalBoss, // the last boss has just died: the short "the last boss falls" card (every client) // souls rising at the lighthouse after a settlement (drawn by Weapons)
        ChestOpen, // a chest opened: Arg the best rarity inside
        BossMusic, // a fortress boss fight: Arg 1 start, 0 stop
        RoundEnd, // Arg 1 victory, 0 defeat: the full-screen end beat
        Fuse } // a bomber's fuse lit (Siege.FuseTime to go): PieceId the fish

    // Host -> everyone: a visual effect (and for Announce, a banner: Key = string key, Arg = number). Who = the player whose weapon did it.
    public struct FxEvent : IBroadcast { public FxKind Kind; public Vector3 From; public Vector3 To; public int PieceId; public string Key; public int Arg; public int Who; }
    // A whole frame's worth of effects in one broadcast (a late wave makes dozens a second: one message each was most of the
    // mod's traffic and the client ran a handler per effect).
    public struct FxBatch : IBroadcast { public FxEvent[] Events; }

    // All transient visuals in one place, drawn on every client from host broadcasts.
    public class Fx : MonoBehaviour
    {
        private static Fx _instance;
        private NetworkManager _registeredOn;
        private Material _tracer, _ball, _blast, _spit, _bolt, _zap, _fire, _heal, _dust, _water, _stone, _shell, _sniperTracer, _flameCore;
        private static Mesh _flameMesh;

        static Fx()
        {
            GenericWriter<FxEvent>.SetWrite((w, v) => { w.WriteByte((byte)v.Kind); w.WriteVector3(v.From); w.WriteVector3(v.To); w.WriteInt32(v.PieceId); w.WriteString(v.Key ?? ""); w.WriteInt32(v.Arg); w.WriteInt32(v.Who); });
            GenericReader<FxEvent>.SetRead(r => new FxEvent { Kind = (FxKind)r.ReadByte(), From = r.ReadVector3(), To = r.ReadVector3(), PieceId = r.ReadInt32(), Key = r.ReadString(), Arg = r.ReadInt32(), Who = r.ReadInt32() });
            GenericWriter<FxBatch>.SetWrite((w, v) =>
            {
                int n = v.Events?.Length ?? 0;
                w.WriteInt32(n);
                for (int i = 0; i < n; i++)
                {
                    var e = v.Events[i];
                    w.WriteByte((byte)e.Kind); w.WriteVector3(e.From); w.WriteVector3(e.To); w.WriteInt32(e.PieceId);
                    w.WriteString(e.Key ?? ""); w.WriteInt32(e.Arg); w.WriteInt32(e.Who);
                }
            });
            GenericReader<FxBatch>.SetRead(r =>
            {
                int n = r.ReadInt32();
                var arr = new FxEvent[n];
                for (int i = 0; i < n; i++)
                    arr[i] = new FxEvent { Kind = (FxKind)r.ReadByte(), From = r.ReadVector3(), To = r.ReadVector3(), PieceId = r.ReadInt32(), Key = r.ReadString(), Arg = r.ReadInt32(), Who = r.ReadInt32() };
                return new FxBatch { Events = arr };
            });
        }

        private void Awake() => _instance = this;
        public static Fx Instance => _instance;

        private static readonly List<FxEvent> _pending = new List<FxEvent>(128); // host: this frame's effects, sent once
        public static int Queued; // read and cleared by PerfLog: effects the host produced in the last window

        // Host: queue one for everyone (the host's own client receives it too, at the end of the frame).
        public static void Queue(FxEvent e)
        {
            if (!InstanceFinder.IsServerStarted) return;
            Queued++;
            _pending.Add(e);
            if (_pending.Count >= 256) Flush();
        }

        // One broadcast per frame at most: a wave's hits, burns and shots arrive together instead of as dozens of messages.
        private static void Flush()
        {
            if (_pending.Count == 0) return;
            var events = _pending.ToArray();
            _pending.Clear();
            InstanceFinder.ServerManager.Broadcast(new FxBatch { Events = events });
        }

        // Host: send to everyone (the host's own client receives it too).
        // `damage` > 0 shows the game's own damage number where it lands.
        public static void Send(FxKind kind, Vector3 from, Vector3 to, int pieceId = 0, int damage = 0, int who = 0, string key = null) =>
            Queue(new FxEvent { Kind = kind, From = from, To = to, PieceId = pieceId, Arg = damage, Who = who, Key = key });

        // The game's own sounds (see BepInEx/fortress-sounds.txt for every name).
        public static void Sound(string clip, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium)
        {
            // These are looping grill/fire assets: one-shot playback left a long burning noise hanging in the air (Owen: the
            // lighthouse had a fire crackle nobody could place). Short fire cues only now.
            if (clip == "SizzlingLoop" || clip == "FireLoop") return;
            if (!MayPlay(clip, Time.unscaledTime)) return;
            try { AudioManager.PlayClipAt(clip, at, true, dist, volume); } catch { }
        }

        public static void SoundRandom(string prefix, int min, int max, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium)
        {
            if (!MayPlay(prefix, Time.unscaledTime)) return;
            try { AudioManager.PlayRandomClipAt(prefix, min, max, at, true, dist, volume); } catch { }
        }

        // Rapid fire: the game's player holds every clip family back 0.1 s (AudioManager's minDelay), which left a 12-a-second
        // gun sounding every second or third shot. These skip both holds; the callers keep their own beat and count.
        public static void Rapid(string prefix, int min, int max, Vector3 at, float volume)
        {
            try { AudioManager.PlayRandomClipAt(prefix, min, max, at, true, AudioDistance.Medium, volume, 0f); } catch { }
        }

        // Every sound of ours goes through here: one clip (a random one's whole family) at most every SameGap, and at most
        // PerWindow starting in any Window (dozens of hits and turret shots at once crackled: 「我甚至遇到還出現那個破音的」).
        public const float SameGap = 0.06f, Window = 0.25f;
        public const int PerWindow = 12;
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        private static float _windowAt = float.MinValue;
        private static int _inWindow;
        public static bool MayPlay(string clip, float now)
        {
            if (LastPlayed.TryGetValue(clip ?? "", out float last) && now - last < SameGap && now >= last) return false;
            if (now - _windowAt >= Window || now < _windowAt) { _windowAt = now; _inWindow = 0; }
            if (_inWindow >= PerWindow) return false;
            _inWindow++;
            LastPlayed[clip ?? ""] = now;
            return true;
        }

        // Where someone died (what they carried lies there, and is hard to find in the grass): a tall gold pillar for two
        // minutes, and a marker with the distance (Tips). Gone early once its owner has come back to it.
        public static readonly List<(Vector3 pos, float until, string who)> DeathMarks = new List<(Vector3, float, string)>();
        private IEnumerator DeathPillar(Vector3 at, int playerId)
        {
            var who = PlayerManager.Players.FirstOrDefault(p => p && p.ObjectId == playerId);
            string name = who ? Loot.NameOf(who) : "";
            var mark = (at, Time.time + 120f, name);
            DeathMarks.Add(mark);
            var pillar = WeaponFx.Piece(WeaponFx.Cyl, BossVfx.Glow(new Color(1f, 0.78f, 0.25f), 4f));
            pillar.position = at; // Cyl runs 0..1 up from its foot: it stood 15-30 m up, over the island, not at the body
            pillar.localScale = new Vector3(0.18f, 15f, 0.18f);
            bool backAlive = false;
            for (float t = 0f; t < 120f && pillar; t += 0.25f)
            {
                pillar.localScale = new Vector3(0.18f + 0.04f * Mathf.Sin(t * 3f), 15f, 0.18f + 0.04f * Mathf.Sin(t * 3f));
                // its owner alive again and standing by it: they've got their things
                if (who && who.Transform && !who.Dying.IsDead) { if (backAlive && (who.Transform.position - at).Flat().magnitude < 3f) break; backAlive = true; }
                yield return new WaitForSeconds(0.25f);
            }
            DeathMarks.Remove(mark);
            if (pillar) Destroy(pillar.gameObject);
        }

        // Turrets lost, said once for all those gone within half a second (a bomber can take three at once), in red, bottom left.
        private readonly List<string> _lost = new List<string>();
        private bool _flushing;
        private System.Collections.IEnumerator FlushLost()
        {
            _flushing = true;
            yield return new WaitForSeconds(0.5f);
            ChatManager.ChatMessage($"<color=#ff6b5e>{L10n.T("towersLost", string.Join("、", _lost))}</color>");
            Vector3 me = Player.LocalPlayer && Player.LocalPlayer.Transform ? Player.LocalPlayer.Transform.position : Vector3.zero;
            Sound("Error", me, 0.6f);
            _lost.Clear();
            _flushing = false;
        }

        public static void Announce(string key, int arg = 0) => Queue(new FxEvent { Kind = FxKind.Announce, Key = key, Arg = arg });

        // A boss's fight (BossBrain.Report), in everyone's chat in their own language: seconds, the players' share of the
        // damage, and how much of its health was taken (From.x) if it got away.
        public static void BossFight(EnemyKind kind, bool down, int seconds, int playersPct, int takenPct)
        {
            Queue(new FxEvent { Kind = FxKind.Announce, Key = down ? "bossFightDown" : "bossFightCrashed", Arg = seconds, PieceId = playersPct, Who = (int)kind, From = new Vector3(takenPct, 0f, 0f) });
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<FxBatch>((b, ch) => { if (b.Events == null) return; for (int i = 0; i < b.Events.Length; i++) Play(b.Events[i]); });
            }
            if (InstanceFinder.IsServerStarted && _pending.Count > 0) Flush(); // the whole frame's effects, once
        }

        private Canvas _endCanvas, _pulseCanvas;
        private UnityEngine.UI.Image _pulse;

        // A short full-screen colour pulse for the Lv20 overdrive; cached so it costs one canvas, not one per blast.
        private IEnumerator ScreenPulse(Color colour, float strength)
        {
            if (!Hud.Font) yield break;
            var canvas = Hud.Canvas("FortressPulse", 880, ref _pulseCanvas);
            if (!_pulse)
            {
                _pulse = Hud.Box(canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 3000f), colour, 0f);
                _pulse.raycastTarget = false;
            }
            for (float t = 0f; t < 0.28f; t += Time.unscaledDeltaTime)
            {
                float k = 1f - t / 0.28f;
                _pulse.color = new Color(colour.r, colour.g, colour.b, strength * k * k);
                yield return null;
            }
            _pulse.color = new Color(colour.r, colour.g, colour.b, 0f);
        }

        // The last boss dying gets its own short, quiet beat before the victory camera: letterbox in, gold flash, "the last
        // boss falls". Local only: every client draws it from the one host broadcast.
        private IEnumerator FinalBossCard(Vector3 at)
        {
            var canvas = Hud.Canvas("FortressEnding", 900, ref _endCanvas);
            canvas.gameObject.SetActive(true);
            var holder = new GameObject("FinalBossCard", typeof(RectTransform), typeof(CanvasGroup));
            holder.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)holder.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var group = holder.GetComponent<CanvasGroup>();
            var flash = Hud.Box(holder.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 3000f), new Color(1f, 0.85f, 0.4f, 0.28f), 0f);
            flash.raycastTarget = false;
            var top = Hud.Box(holder.transform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(5000f, 230f), new Color(0f, 0f, 0f, 0.92f), 0f);
            var bottom = Hud.Box(holder.transform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(5000f, 230f), new Color(0f, 0f, 0f, 0.92f), 0f);
            var title = Hud.Label(holder.transform, L10n.T("finalBossDown"), 86f, new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(1800f, 150f), TMPro.TextAlignmentOptions.Center, bold: true);
            title.color = Hud.Gold; title.enableAutoSizing = true; title.fontSizeMin = 46f; title.fontSizeMax = 96f;
            var sub = Hud.Label(holder.transform, L10n.T("finalBossSub"), 30f, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1600f, 70f), TMPro.TextAlignmentOptions.Center, Hud.Muted, bold: true);
            Sound("CasinoWinEpic", at, 1f, AudioDistance.Long);
            WeaponFx.Game("BigExplosion", at, Vector3.up);
            for (float t = 0f; t < 3.2f; t += Time.unscaledDeltaTime)
            {
                float k = t / 3.2f;
                group.alpha = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((3.2f - t) / 0.5f);
                top.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(230f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f))));
                bottom.rectTransform.anchoredPosition = new Vector2(0f, -Mathf.Lerp(230f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f))));
                title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.5f)));
                flash.color = new Color(1f, 0.85f, 0.4f, Mathf.Lerp(0.28f, 0f, Mathf.Clamp01(t / 0.8f)));
                yield return null;
            }
            if (holder) Destroy(holder);
        }

        // The turret that fired: its level makes the shot/impact bigger and louder (Owen: upgrades should feel crazy).
        private static int TurretLevel(int id) => BuildSystem.Instance && BuildSystem.Instance.TryGetState(id, out var st) ? st.Level : 1;

        // Flame's branch palette: Inferno red, Wide orange, Hellfire red-orange, Melt white-hot, Wall of fire gold, Blue flame blue.
        private static Color FlameColour(int id)
        {
            int b = BuildSystem.Instance && BuildSystem.Instance.TryGetState(id, out var st) ? st.Branch : 0;
            switch (b)
            {
                case 1: return new Color(1f, 0.18f, 0.06f);
                case 2: return new Color(1f, 0.48f, 0.08f);
                case 3: return new Color(1f, 0.10f, 0.03f);
                case 4: return new Color(1f, 0.85f, 0.45f);
                case 5: return new Color(1f, 0.62f, 0.10f);
                case 6: return new Color(0.18f, 0.62f, 1f); // 藍焰
                default: return new Color(1f, 0.52f, 0.12f);
            }
        }

        private void Play(FxEvent e)
        {
            if (e.Kind == FxKind.FinalBoss) { StartCoroutine(FinalBossCard(e.To)); return; }
            if (e.Kind == FxKind.DeathMark)
            {
                StartCoroutine(DeathPillar(e.From, e.Who));
                // No getting up on your own in a round (NoSelfRespawn): the one down waits, the others are told how to help.
                var down = PlayerManager.Players.FirstOrDefault(p => p && p.ObjectId == e.Who);
                if (down == Player.LocalPlayer) ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("downSelf"));
                else if (down) ChatManager.ChatMessage(L10n.T("prefix") + " " + L10n.T("downOther", Loot.NameOf(down)));
                return;
            }
            if (e.Kind == FxKind.PieceLost) { _lost.Add(BuildSystem.PieceName((PieceType)e.Arg, e.Who) + " Lv" + e.Who); if (!_flushing) StartCoroutine(FlushLost()); return; }
            if (e.Kind == FxKind.Announce)
            {
                if (e.Key == "bossFightDown" || e.Key == "bossFightCrashed")
                {
                    ChatManager.ChatMessage(L10n.T(e.Key, L10n.T("bossName" + (EnemyKind)e.Who), e.Arg, e.PieceId, 100 - e.PieceId, Mathf.RoundToInt(e.From.x)));
                    return;
                }
                if (FortressIsland.Instance) FortressIsland.Instance.Banner(L10n.T(e.Key, e.Arg), 3.5f);
                Vector3 me = Player.LocalPlayer && Player.LocalPlayer.Transform ? Player.LocalPlayer.Transform.position : Vector3.zero;
                switch (e.Key)
                {
                    case "clearedBanner": Sound("WinRound", me, 0.8f, AudioDistance.Long); break;
                    default: Sound("QuestReceived", me, 0.9f, AudioDistance.Long); break;
                }
                return;
            }
            if (e.Kind == FxKind.Absorber)
            {
                // Its moves that leave the gun start at the muzzle as this screen sees it (the weapons' rule), not where the host guessed.
                if (Weapons.Instance && (e.Key == "suck" || e.Key == "shot" || e.Key == "warden" || e.Key == "angler" || e.Key == "eye")) e.From = Weapons.Instance.Origin(e);
                if (BossFx.Instance) BossMoves.PlayAbsorber(BossFx.Instance, e);
                return;
            }
            if (e.Kind >= FxKind.Beam && e.Kind <= FxKind.WeaponHit || e.Kind >= FxKind.Net && e.Kind <= FxKind.Claw || e.Kind == FxKind.Rocket || e.Kind == FxKind.Drip || e.Kind == FxKind.Gatling) { if (Weapons.Instance) Weapons.Instance.PlayFx(e); return; } // PieceId there is a fish or a shark, Arg is not damage
            if (e.Kind == FxKind.Absorb) { StartCoroutine(Swallow.View(e)); return; }
            if (e.Kind == FxKind.BossIntro) { Reveal.Instance.Boss((EnemyKind)e.Arg, e.To, e.From, e.PieceId); return; }
            if (e.Kind == FxKind.BossFx) { if (BossFx.Instance) BossFx.Instance.Play(e); return; }
            if (e.Kind == FxKind.Fuse) { StartCoroutine(Fuse(e.PieceId, e.From)); return; }
            if (e.Arg > 0 && e.Kind != FxKind.Proc && e.Kind != FxKind.Decoy) { try { PlayerUI.AddDamageNumber(e.To + Vector3.up * 0.8f, e.Arg, false); } catch { } }
            if (!_tracer)
            {
                // Energy (tracers, zaps, blasts, fire, heals) glows in the game's own emissive particle look, shared with the
                // weapons' and the bosses' (BossVfx.Glow caches one material a colour); matter (balls, shells, stone, spit,
                // dust, water) stays lit.
                _tracer = BossVfx.Glow(new Color(1f, 0.82f, 0.35f), 7f);
                _ball = Models.Make(new Color(0.15f, 0.15f, 0.15f));
                _blast = BossVfx.Glow(new Color(1f, 0.55f, 0.12f), 6f);
                _spit = Models.Make(new Color(0.35f, 0.85f, 0.4f), new Color(0.2f, 0.6f, 0.25f));
                _bolt = Models.Make(new Color(0.75f, 0.78f, 0.82f));
                _zap = BossVfx.Glow(new Color(0.55f, 0.85f, 1f), 8f);
                _fire = BossVfx.Glow(new Color(1f, 0.42f, 0.08f), 5f);
                _heal = BossVfx.Glow(new Color(0.45f, 1f, 0.5f), 5f);
                _dust = Models.Make(new Color(0.62f, 0.5f, 0.36f));
                _water = Models.Make(new Color(0.55f, 0.8f, 1f), new Color(0.4f, 0.7f, 1f));
                _stone = Models.Make(new Color(0.32f, 0.32f, 0.34f));
                _shell = Models.Make(new Color(0.18f, 0.16f, 0.15f), new Color(0.4f, 0.12f, 0.05f));
                _sniperTracer = BossVfx.Glow(new Color(0.6f, 0.92f, 1f), 8f);
                _flameCore = Models.Make(new Color(1f, 0.9f, 0.45f), new Color(1f, 0.7f, 0.15f));
                _flameMesh = BuildFlameMesh();
            }
            if (e.PieceId != 0 && e.Kind != FxKind.Scoop) AimTurret(e.PieceId, e.To); // for a scoop it's the body being netted
            switch (e.Kind)
            {
                case FxKind.Tracer:
                    if (e.PieceId != 0) Rattling(e.PieceId, e.From, e.To);
                    else StartCoroutine(Tracer(e.From, e.To, e.Key)); // a second barrel's fish: the rattle above is its sound
                    break;
                case FxKind.Cannonball: StartCoroutine(Arc(e.From, e.To, e.Key == "Cannon" ? _stone : _ball, (e.Key == "Cannon" ? 0.46f : 0.35f) * (1f + 0.035f * (TurretLevel(e.PieceId) - 1)), 0.35f, 1.5f, true, e.Key, TurretLevel(e.PieceId))); SoundRandom("ShotgunFireOutside_0", 1, 4, e.From, 0.8f); break;
                case FxKind.Spit: StartCoroutine(Arc(e.From, e.To, _spit, 0.3f, SpitFlight, 6f, false)); SoundRandom("PiranhaVomit_V", 1, 3, e.From, 0.8f); break; // higher lob for the spitter
                case FxKind.Blast:
                    // Weapon explosions carry their real radius (cm) in Arg; turrets keep their level-scaled default.
                    float blastRadius = e.Arg > 0 ? e.Arg / 100f : BlastDefault * (e.PieceId != 0 ? 1f + 0.055f * (TurretLevel(e.PieceId) - 1) : 1f);
                    Explosion(e.To, blastRadius);
                    if (e.PieceId != 0 && TurretLevel(e.PieceId) >= 20) StartCoroutine(ScreenPulse(new Color(1f, 0.55f, 0.12f), 0.22f));
                    Sound("Explosion", e.To, e.PieceId != 0 ? 1f : 0.85f, AudioDistance.Long);
                    break;
                case FxKind.Harpoon: StartCoroutine(Bolt(e.From, e.To, e.Key)); SoundRandom("BowShoot_0", 1, 4, e.From, 0.9f); break;
                case FxKind.Mortar: StartCoroutine(Arc(e.From, e.To, _shell, 0.45f * (1f + 0.03f * (TurretLevel(e.PieceId) - 1)), MortarFlight, 9f, true, e.Key, TurretLevel(e.PieceId))); Sound("BoatFire", e.From, 0.8f); break;
                case FxKind.Zap: StartCoroutine(Zap(e.From, e.To)); if (e.PieceId != 0) Sound("Ignite", e.From, 0.6f); break;
                case FxKind.Scoop:
                    if (!(Weapons.Instance && Weapons.Instance.OnScoop(e))) StartCoroutine(Fly(e.From, e.To + Vector3.up * 1.2f)); // into my own net: it shows in the bag
                    SoundRandom("PickUp_V", 1, 3, e.To, 0.7f, AudioDistance.Short);
                    break;
                case FxKind.Sell: SoundRandom("Sell_0", 1, 3, e.To, 1f); StartCoroutine(Blast(e.To + Vector3.up, 1.6f, _tracer)); break;
                case FxKind.Burn: if (!ClientSettings.Reduced) StartCoroutine(Blast(e.To + Random.insideUnitSphere * 1.5f, 1.4f, _fire)); break; // every burning fish, twice a second
                case FxKind.Heal: StartCoroutine(Zap(e.From, e.To, _heal)); if (!ClientSettings.Reduced) StartCoroutine(Blast(e.To, 0.9f, _heal)); Sound("QuestReceived", e.To, 0.25f, AudioDistance.Short); break;
                case FxKind.Mend: AimTurret(e.PieceId, e.To); StartCoroutine(Zap(e.From, e.To, _heal)); if (!ClientSettings.Reduced) StartCoroutine(Blast(e.To, 0.6f, _heal)); break; // quiet: it goes off every other second
                case FxKind.Dust: StartCoroutine(Blast(e.To, 2.2f, _dust)); SoundRandom("ItemImpactHeavy_V", 1, 3, e.To, 0.8f); break;
                case FxKind.Wave: Splash(e.To, 3f); SoundRandom("WhaleLanding_0", 1, 3, e.To, 1f, AudioDistance.Long); break;
                case FxKind.Decoy: StartCoroutine(Duck(e.To, e.Arg)); break;
                case FxKind.Proc: GearCards.Instance.Proc((Gear)e.Arg, e.To); break;
                case FxKind.Snipe: StartCoroutine(Tracer(e.From, e.To, "Sniper", TurretLevel(e.PieceId))); SoundRandom("SniperrifleFireOutside_V", 1, 4, e.From, 0.9f, AudioDistance.Long); break;
                case FxKind.Flame:
                    StartCoroutine(FlameJet(e.From, e.To, FlameColour(e.PieceId)));
                    if (Random.value < 0.3f) Sound("Ignite", e.From, 0.5f);
                    break;
                case FxKind.Victory: StartCoroutine(Fireworks(e.To)); Reveal.Instance.Victory(e.To - Vector3.up * 6f); break;
                case FxKind.Crash:
                    try { ParticleManager.Play("BigExplosion", e.To, Vector3.up); } catch { }
                    Sound("Explosion", e.To, 1f, AudioDistance.Long);
                    SoundRandom("WhaleLanding_0", 1, 3, e.To, 1f, AudioDistance.Long);
                    break;
                case FxKind.ChestOpen: StartCoroutine(ChestBurst(e.To, (Rarity)e.Arg)); break;
                case FxKind.BossMusic: BossFightMusic(e.Arg > 0); break;
                case FxKind.RoundEnd: StartCoroutine(RoundEndScreen(e.Arg > 0)); break;
            }
        }

        // OVER 99: confetti bursts round the top of the lighthouse for a few seconds, with the casino's big win.
        private IEnumerator Fireworks(Vector3 at)
        {
            Sound("CasinoWinEpic", at, 1f, AudioDistance.Long);
            string[] kinds = { "Confetti", "RedConfetti", "GreenConfetti" };
            for (int i = 0; i < 14; i++)
            {
                Vector3 p = at + new Vector3(Random.Range(-6f, 6f), Random.Range(0f, 5f), Random.Range(-6f, 6f));
                try { ParticleManager.Play(kinds[i % kinds.Length], p, Vector3.up); } catch { }
                if (i % 3 == 0) SoundRandom("ItemImpactHeavy_V", 1, 3, p, 0.4f);
                yield return new WaitForSeconds(0.35f);
            }
        }

        public const float SpitFlight = 0.8f;
        public const float MortarFlight = 1.3f;

        private static void AimTurret(int pieceId, Vector3 target)
        {
            var piece = BuildSystem.Instance.PieceObject(pieceId);
            var head = piece ? piece.transform.Find("Head") : null;
            if (!head) return;
            Vector3 dir = target - head.position; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) head.rotation = Quaternion.LookRotation(dir);
        }

        // The turrets' tracers, zaps, balls and blasts: a few hundred a second in a big fort, so they're kept for the next
        // one (no collider to make and destroy each time, as CreatePrimitive did). Put back with Done.
        private static readonly Stack<GameObject> Cubes = new Stack<GameObject>(), Spheres = new Stack<GameObject>();
        private static GameObject Shape(PrimitiveType type, Material mat)
        {
            var pool = type == PrimitiveType.Cube ? Cubes : Spheres;
            GameObject go = null;
            while (pool.Count > 0 && !go) go = pool.Pop();
            if (!go)
            {
                go = WeaponFx.Piece(type == PrimitiveType.Cube ? WeaponFx.Cube : WeaponFx.Ball, mat).gameObject;
                go.name = type == PrimitiveType.Cube ? "FortressFxCube" : "FortressFxBall";
                DontDestroyOnLoad(go);
            }
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one;
            go.SetActive(true);
            return go;
        }

        private static void Done(GameObject go)
        {
            if (!go) return;
            go.SetActive(false);
            var pool = go.name == "FortressFxCube" ? Cubes : Spheres;
            if (pool.Count < 200) pool.Push(go); else Destroy(go);
        }

        // Machine-gun turrets rattle at a steady beat on every client. The host's tracer only says "still firing at X" every
        // 0.15 s; a shot sound per tracer, through the game's 0.1 s hold, came out ragged (Owen: 「機關槍射擊的聲音跟節奏現在不像
        // 機關槍」). The beat follows the turret's own fire rate, 8 to 14 rounds a second.
        private sealed class Rattle { public Vector3 From, To; public float Until, Beat; public int Level; }
        private readonly Dictionary<int, Rattle> _rattles = new Dictionary<int, Rattle>();
        private const int AudibleRattles = 2; // the nearest ones: every turret on the island at once crackled

        private void Rattling(int id, Vector3 from, Vector3 to)
        {
            if (!_rattles.TryGetValue(id, out var r))
            {
                r = new Rattle { Level = 1, Beat = 0.12f };
                if (BuildSystem.Instance && BuildSystem.Instance.TryGetState(id, out var st))
                {
                    r.Level = st.Level;
                    r.Beat = Mathf.Clamp(Catalog.Turret(st.Type, st.Level, st.Branch).Interval, 0.07f, 0.12f);
                }
                _rattles[id] = r;
                StartCoroutine(RattleLoop(id, r));
            }
            r.From = from; r.To = to; r.Until = Time.time + 0.25f;
        }

        private IEnumerator RattleLoop(int id, Rattle r)
        {
            float next = Time.time;
            while (Time.time < r.Until)
            {
                if (Time.time >= next)
                {
                    next += r.Beat;
                    if (next < Time.time) next = Time.time + r.Beat; // after a hitch it carries on, it doesn't catch up
                    StartCoroutine(Tracer(r.From, r.To + Random.insideUnitSphere * 0.25f, "Gun", r.Level));
                    if (Audible(r)) Rapid("AssaultrifleFireOutside_0", 1, 4, r.From, 0.3f);
                }
                yield return null;
            }
            _rattles.Remove(id);
        }

        private bool Audible(Rattle r)
        {
            var cam = GameInfo.CurCamera;
            if (!cam) return false;
            Vector3 at = cam.transform.position;
            float d = (r.From - at).sqrMagnitude;
            int closer = 0;
            foreach (var o in _rattles.Values)
                if (o != r && (o.From - at).sqrMagnitude < d && ++closer >= AudibleRattles) return false;
            return true;
        }

        // A lit bomber: it blinks red three times and its head spits sparks, hissing, until it goes off.
        private IEnumerator Fuse(int fish, Vector3 at)
        {
            Sound("Ignite", at, 1f);
            var red = new Color(1f, 0.15f, 0.1f);
            float end = Time.time + Siege.FuseTime, blink = 0f;
            while (Time.time < end)
            {
                var body = BossFx.BodyOf(fish);
                if (!body) yield break; // shot before it went off
                Vector3 top = body.position + Vector3.up * 0.7f;
                if (Time.time >= blink) { blink = Time.time + Siege.FuseTime / 3f; BossVfx.Flash(top, 1.8f, red, 0.12f); }
                WeaponFx.Emit(WeaponFx.Sparks("Gold"), top, 2, Vector3.up);
                yield return null;
            }
        }

        private IEnumerator Tracer(Vector3 from, Vector3 to, string key = null, int level = 1)
        {
            float grow = 1f + 0.045f * (Mathf.Max(1, level) - 1);
            Vector3 d = to - from;
            bool sniper = key == "Sniper", gun = key == "Gun";
            var go = Shape(PrimitiveType.Cube, sniper ? _sniperTracer : _tracer);
            go.transform.SetPositionAndRotation(from + d * 0.5f, Quaternion.LookRotation(d));
            float w = (sniper ? 0.035f : gun ? 0.06f : 0.05f) * grow;
            go.transform.localScale = new Vector3(w, w, d.magnitude);
            // Sparks leave the muzzle; where it lands, a few chips fly back out of the fish (the gatling's hit look).
            if (gun) { WeaponFx.Emit(WeaponFx.Sparks("Gold"), from, Mathf.RoundToInt(2 + level * 0.15f), d.normalized); WeaponFx.Emit(WeaponFx.Sparks("Gold"), to, 2, -d.normalized + Vector3.up * 0.5f); } // a dozen rounds a second now
            else if (sniper) { WeaponFx.Emit(WeaponFx.Sparks("Cyan"), from, Mathf.RoundToInt(6 + level * 0.5f), d.normalized); WeaponFx.Emit(WeaponFx.Sparks("Cyan"), to, 6, -d.normalized + Vector3.up * 0.5f); }
            yield return new WaitForSeconds(sniper ? 0.09f : 0.05f);
            Done(go);
        }

        // Net scoop: a little fish-coloured blob hops from the corpse into the player.
        private IEnumerator Fly(Vector3 from, Vector3 to)
        {
            var go = Shape(PrimitiveType.Sphere, _spit);
            go.transform.localScale = Vector3.one * 0.35f;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                float k = t / 0.3f;
                go.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
                go.transform.localScale = Vector3.one * 0.35f * (1f - k * 0.6f);
                yield return null;
            }
            Done(go);
        }

        // Harpoon: a thin bolt flying the whole line, its rope paying out behind it from the turret (it used to be a bare
        // bar, nothing to say what fired it), a spray of water where it ends, then the rope gone with it.
        private IEnumerator Bolt(Vector3 from, Vector3 to, string key = null)
        {
            var go = Shape(PrimitiveType.Cube, _bolt);
            go.transform.localScale = key == "Harpoon" ? new Vector3(0.06f, 0.06f, 1.8f) : new Vector3(0.08f, 0.08f, 1.4f);
            go.transform.rotation = Quaternion.LookRotation(to - from);
            var rope = key == "Harpoon" ? WeaponFx.Line("HarpoonRope", WeaponFx.RopeMat, 0.035f) : null;
            if (rope) { rope.positionCount = 2; rope.SetPosition(0, from); rope.SetPosition(1, from); }
            const float flight = 0.3f;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                go.transform.position = Vector3.Lerp(from, to, t / flight);
                if (rope) rope.SetPosition(1, go.transform.position - go.transform.forward * 0.9f);
                if (key == "Harpoon" && Random.value < 0.25f) WeaponFx.Emit(WeaponFx.Sparks("Gray"), go.transform.position, 1, Vector3.up);
                yield return null;
            }
            WeaponFx.Emit(WeaponFx.Drops, to, 6, Vector3.up);
            yield return new WaitForSeconds(0.08f);
            Done(go);
            if (rope) Destroy(rope.gameObject);
        }

        // Tesla: a jagged bright line that flickers out.
        // The decoy: a big rubber duck that bobs and squeaks, then deflates.
        private IEnumerator Duck(Vector3 at, float seconds)
        {
            var go = new GameObject("FortressDecoy");
            go.transform.position = at;
            Models.Attach("Decoy", go.transform, new Vector3(0.8f, 0.8f, 0.8f), new Vector3(0f, 0.4f, 0f));
            Sound("QuestReceived", at, 0.6f);
            for (float t = 0f; t < seconds && go; t += Time.deltaTime)
            {
                go.transform.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(t * 9f)) * Mathf.Min(1f, (seconds - t) * 2f);
                go.transform.rotation = Quaternion.Euler(0f, Mathf.Sin(t * 2f) * 25f, Mathf.Sin(t * 5f) * 6f);
                yield return null;
            }
            if (go) Destroy(go);
        }

        private IEnumerator Zap(Vector3 from, Vector3 to, Material mat = null)
        {
            var parts = new GameObject[3];
            Vector3 prev = from;
            for (int i = 0; i < 3; i++)
            {
                Vector3 next = i == 2 ? to : Vector3.Lerp(from, to, (i + 1) / 3f) + Random.insideUnitSphere * 0.5f;
                var go = Shape(PrimitiveType.Cube, mat ? mat : _zap);
                Vector3 d = next - prev;
                go.transform.SetPositionAndRotation(prev + d * 0.5f, Quaternion.LookRotation(d));
                go.transform.localScale = new Vector3(0.07f, 0.07f, d.magnitude);
                parts[i] = go; prev = next;
            }
            yield return new WaitForSeconds(0.09f);
            foreach (var go in parts) Done(go);
        }

        private IEnumerator Arc(Vector3 from, Vector3 to, Material mat, float size, float flight, float height, bool blastAtEnd, string key = null, int level = 1)
        {
            var ball = Shape(PrimitiveType.Sphere, mat);
            ball.transform.localScale = Vector3.one * size;
            float next = 0f;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                float k = t / flight;
                ball.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * height;
                if (t >= next)
                {
                    next = t + 0.06f;
                    if (key == "Cannon") { WeaponFx.Emit(WeaponFx.Sparks("Orange"), ball.transform.position, 3, Vector3.up); WeaponFx.Emit(WeaponFx.Dust, ball.transform.position, 1, Vector3.up); }
                    else if (key == "Mortar") WeaponFx.Emit(WeaponFx.Dust, ball.transform.position, 2, Vector3.up);
                }
                yield return null;
            }
            Done(ball);
            if (blastAtEnd) { Sound("Explosion", to, 0.6f); Explosion(to, Mathf.Min(3.5f, 1.6f * (1f + 0.05f * (Mathf.Max(1, level) - 1)))); }
            else { SoundRandom("ItemHitWaterMedium_V", 1, 3, to, 0.8f); yield return Blast(to, 1.2f, _spit); }
        }

        // ---------- explosions and splashes: layered so their size reads on the ground, never a ball over the screen ----------

        // A blast's look from its real radius (m): the flash in the middle stops growing at BlastCoreMax (a Lv20 cannon used
        // to be a 7 m solid orange ball in your face), the ring of shock on the ground is the radius itself, and the sparks
        // and smoke are bounded. BlastDefault: a turret's at Lv1 when the host sends no radius.
        public const float BlastCoreMax = 2.4f, BlastDefault = 2f;
        public static (float flash, float ring, int sparks, int smoke) BlastLook(float radius) =>
            (Mathf.Clamp(radius * 1.1f, 0.8f, BlastCoreMax), Mathf.Max(0.6f, radius),
             Mathf.Clamp(Mathf.RoundToInt(8f + radius * 4f), 8, 32), Mathf.Clamp(Mathf.RoundToInt(3f + radius * 2f), 3, 10));

        private static readonly Color BlastOrange = new Color(1f, 0.55f, 0.15f), Smoke = new Color(0.36f, 0.33f, 0.3f, 0.85f), SeaBlue = new Color(0.5f, 0.78f, 1f);

        public static void Explosion(Vector3 at, float radius)
        {
            var look = BlastLook(radius);
            BossVfx.Flash(at + Vector3.up * 0.3f, look.flash, BlastOrange, 0.22f);
            BossVfx.Shock(at, look.ring, BlastOrange, 0.45f);
            WeaponFx.Emit(WeaponFx.Sparks("Orange"), at + Vector3.up * 0.3f, look.sparks, Vector3.up);
            float puff = Mathf.Min(1.6f, 0.6f + radius * 0.25f);
            for (int i = 0; i < look.smoke; i++)
                WeaponFx.Emit(WeaponFx.Dust, at + Vector3.up * 0.4f, 1, Vector3.up, Smoke, Random.Range(0.6f, 1.1f) * puff,
                              Random.onUnitSphere.Flat().normalized * Random.Range(1f, 2.5f) + Vector3.up * Random.Range(1f, 2.5f), Random.Range(0.6f, 1f));
        }

        // Water coming down (the whale's wave, a tide): a ring on the sea, foam and drops thrown up, the game's splash.
        public static void Splash(Vector3 at, float radius)
        {
            BossVfx.Shock(at, radius, SeaBlue, 0.5f);
            WeaponFx.Emit(WeaponFx.Foam, at + Vector3.up * 0.2f, Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 6, 16), Vector3.up);
            WeaponFx.Emit(WeaponFx.Drops, at + Vector3.up * 0.3f, Mathf.Clamp(Mathf.RoundToInt(radius * 6f), 8, 24), Vector3.up);
            WeaponFx.Game("WaterSplash", at);
        }

        private static bool _bossMusicOn;
        private static string _musicBeforeBoss;
        private static AudioSource MusicSource()
        {
            var mm = Object.FindObjectOfType<MusicManager>();
            return mm ? (AudioSource)typeof(MusicManager).GetField("_musicSourceLooping", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(mm) : null;
        }
        private static void BossFightMusic(bool on)
        {
            if (!Object.FindObjectOfType<MusicManager>()) return;
            if (on)
            {
                if (_bossMusicOn) return;
                _bossMusicOn = true;
                var src = MusicSource();
                _musicBeforeBoss = src && src.clip && src.clip.name != "BossFight1" ? src.clip.name : null;
                MusicManager.PlayMusic("BossFight1", "BossFightIntro");
            }
            else
            {
                if (!_bossMusicOn) return;
                _bossMusicOn = false;
                MusicManager.StopMusic("BossFight1", "BossFightOutro");
                if (Instance) Instance.StartCoroutine(ResumeAfterBoss());
            }
        }
        private static IEnumerator ResumeAfterBoss()
        {
            yield return new WaitForSecondsRealtime(1.4f);
            if (!_bossMusicOn && !string.IsNullOrEmpty(_musicBeforeBoss)) MusicManager.PlayMusic(_musicBeforeBoss);
            _musicBeforeBoss = null;
        }

        private IEnumerator RoundEndScreen(bool victory)
        {
            var colour = victory ? new Color(1f, 0.72f, 0.18f) : new Color(0.65f, 0.06f, 0.04f);
            var fade = Hud.Box(Hud.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 3000f), new Color(colour.r, colour.g, colour.b, 0f), 0f);
            fade.raycastTarget = false;
            for (float t = 0f; t < 0.55f; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(colour.r, colour.g, colour.b, 0.48f * Mathf.Clamp01(t / 0.55f));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.45f);
            for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(colour.r, colour.g, colour.b, 0.48f * (1f - Mathf.Clamp01(t / 0.7f)));
                yield return null;
            }
            if (fade) Object.Destroy(fade.gameObject);
        }

        private IEnumerator ChestBurst(Vector3 at, Rarity rarity)
        {
            var c = GearCatalog.Color(rarity);
            int tier = (int)rarity;
            Fx.Sound(rarity >= Rarity.Legendary ? "CasinoWinEpic" : "ShinyCreatureCaught_0" + Random.Range(1, 4), at, 1f, AudioDistance.Long);
            BossVfx.Flash(at + Vector3.up * 1.4f, 3f + tier * 1.6f, c, 0.55f);
            BossVfx.Shock(at + Vector3.up * 0.25f, 3.5f + tier * 1.3f, c, 0.6f);
            BossFx.Burst(at + Vector3.up * 0.8f, "Gold", 24 + tier * 18, 0.22f);
            for (int i = 0; i < 4; i++)
            {
                WeaponFx.Emit(WeaponFx.Rings, at + Vector3.up * (0.5f + i * 0.25f), 1, Vector3.up, color: c, size: 4f + tier * 1.4f + i * 1.2f);
                yield return new WaitForSeconds(0.06f);
            }
            // A beam of the best thing's colour, so a legendary chest is visible across the island: a bright thread in a
            // see-through shaft, narrowing as it goes up, held a moment, then drawn in slowly and fading (Owen: "光太粗了 可以
            // 細一點點 然後收的非線性慢一點透明一點": it was 0.8-1.85 m of solid glow, gone at once after 0.3 s).
            float w = 0.4f + tier * 0.18f, alpha = 0.55f;
            var shaft = WeaponFx.Line("ChestBeam", BossVfx.Wash(new Color(c.r, c.g, c.b, alpha)), w);
            var core = WeaponFx.Line("ChestBeamCore", BossVfx.Glow(c, 4f + tier), w * 0.28f);
            foreach (var lr in new[] { shaft, core })
            {
                lr.positionCount = 2; lr.SetPosition(0, at + Vector3.up * 0.3f); lr.SetPosition(1, at + Vector3.up * (7f + tier * 2f));
                lr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.45f);
            }
            var shaftMat = shaft.sharedMaterial;
            yield return new WaitForSeconds(0.35f);
            const float draw = 1.1f;
            for (float s = 0f; s < draw && shaft && core; s += Time.deltaTime)
            {
                float k = s / draw, smooth = k * k * (3f - 2f * k);
                shaft.widthMultiplier = w * (1f - smooth);
                core.widthMultiplier = w * 0.28f * (1f - k) * (1f - k);
                shaftMat.color = new Color(c.r, c.g, c.b, alpha * (1f - k) * (1f - k));
                yield return null;
            }
            Object.Destroy(shaftMat);
            if (shaft) Object.Destroy(shaft.gameObject);
            if (core) Object.Destroy(core.gameObject);
        }

        // A real flamethrower cone instead of three balls: an expanding cone mesh plus a spray of short-lived embers.
        private static Mesh BuildFlameMesh()
        {
            const int seg = 14, rings = 5;
            var verts = new Vector3[seg * rings];
            var tris = new int[(seg * (rings - 1) * 2) * 3];
            float[] r = { 0.05f, 0.14f, 0.25f, 0.38f, 0.5f };
            for (int k = 0; k < rings; k++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    verts[k * seg + i] = new Vector3(Mathf.Cos(a) * r[k], Mathf.Sin(a) * r[k], k / (float)(rings - 1));
                }
            int ti = 0;
            for (int k = 0; k < rings - 1; k++)
                for (int i = 0; i < seg; i++)
                {
                    int n = (i + 1) % seg, a = k * seg + i, b = k * seg + n, c = (k + 1) * seg + i, d = (k + 1) * seg + n;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                    tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
                }
            var mesh = new Mesh { name = "FortressFlameCone" };
            mesh.vertices = verts; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private IEnumerator FlameJet(Vector3 from, Vector3 to, Color flame)
        {
            Vector3 d = to - from;
            float dist = Mathf.Max(0.5f, d.magnitude);
            Vector3 dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
            int n = ClientSettings.Reduced ? 10 : 26;
            for (int i = 0; i < n; i++)
            {
                float t = Random.value * 0.28f; // all leave from near the muzzle, then grow/spread outward
                Vector3 p = from + dir * (dist * t) + Random.insideUnitSphere * (0.08f + 0.18f * t);
                Vector3 spread = Quaternion.Euler(Random.Range(-9f, 9f), Random.Range(-9f, 9f), 0f) * dir;
                Color c = Color.Lerp(new Color(1f, 0.95f, 0.6f, 1f), new Color(1f, 0.2f, 0.03f, 1f), Random.value);
                float life = Random.Range(0.22f, 0.45f);
                float speed = dist / life * Random.Range(0.82f, 1.08f); // the plume ends at the turret's range
                Color pc = Color.Lerp(Color.Lerp(Color.white, flame, 0.25f), flame, Random.value);
                WeaponFx.Emit(WeaponFx.FlameFor(flame), p, 1, spread, color: pc, size: Random.Range(0.35f, 0.75f), velocity: spread * speed, life: life);
                if (i % 5 == 0)
                    WeaponFx.Emit(WeaponFx.Dust, Vector3.Lerp(from, to, Random.Range(0.25f, 0.9f)), 1, dir, color: new Color(0.3f, 0.22f, 0.16f, 0.6f), size: Random.Range(0.25f, 0.55f), velocity: dir * Random.Range(3f, 6f), life: Random.Range(0.35f, 0.7f));
            }
            BossVfx.Flash(from + dir * 0.2f, 1.3f, flame, 0.09f);
            yield return null;
        }

        private IEnumerator Blast(Vector3 at, float maxSize, Material mat = null)
        {
            var blast = Shape(PrimitiveType.Sphere, mat ? mat : _blast);
            blast.transform.position = at;
            const float life = 0.18f;
            for (float t = 0f; t < life; t += Time.deltaTime)
            {
                float k = t / life;
                blast.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, maxSize, k) * (1f - k * 0.3f);
                yield return null;
            }
            Done(blast);
        }
    }
}
