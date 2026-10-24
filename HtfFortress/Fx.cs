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
        Soul } // souls rising at the lighthouse after a settlement (drawn by Weapons)

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
        private Material _tracer, _ball, _blast, _spit, _bolt, _zap, _fire, _heal, _dust, _water;

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
        public static void Send(FxKind kind, Vector3 from, Vector3 to, int pieceId = 0, int damage = 0, int who = 0) =>
            Queue(new FxEvent { Kind = kind, From = from, To = to, PieceId = pieceId, Arg = damage, Who = who });

        // The game's own sounds (see BepInEx/fortress-sounds.txt for every name).
        public static void Sound(string clip, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium)
        {
            if (!MayPlay(clip, Time.unscaledTime)) return;
            try { AudioManager.PlayClipAt(clip, at, true, dist, volume); } catch { }
        }

        public static void SoundRandom(string prefix, int min, int max, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium)
        {
            if (!MayPlay(prefix, Time.unscaledTime)) return;
            try { AudioManager.PlayRandomClipAt(prefix, min, max, at, true, dist, volume); } catch { }
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
            pillar.position = at + Vector3.up * 15f;
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

        private void Play(FxEvent e)
        {
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
            if (e.Kind == FxKind.Absorber) { if (BossFx.Instance) BossMoves.PlayAbsorber(BossFx.Instance, e); return; }
            if (e.Kind >= FxKind.Beam && e.Kind <= FxKind.WeaponHit || e.Kind >= FxKind.Net && e.Kind <= FxKind.Claw || e.Kind == FxKind.Rocket || e.Kind == FxKind.Drip || e.Kind == FxKind.Gatling) { if (Weapons.Instance) Weapons.Instance.PlayFx(e); return; } // PieceId there is a fish or a shark, Arg is not damage
            if (e.Kind == FxKind.Absorb) { StartCoroutine(Swallow.View(e)); return; }
            if (e.Kind == FxKind.BossIntro) { Reveal.Instance.Boss((EnemyKind)e.Arg, e.To, e.From, e.PieceId); return; }
            if (e.Kind == FxKind.BossFx) { if (BossFx.Instance) BossFx.Instance.Play(e); return; }
            if (e.Arg > 0 && e.Kind != FxKind.Proc && e.Kind != FxKind.Decoy) { try { PlayerUI.AddDamageNumber(e.To + Vector3.up * 0.8f, e.Arg, false); } catch { } }
            if (!_tracer)
            {
                _tracer = Models.Make(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.7f, 0.2f));
                _ball = Models.Make(new Color(0.15f, 0.15f, 0.15f));
                _blast = Models.Make(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.5f, 0.1f));
                _spit = Models.Make(new Color(0.35f, 0.85f, 0.4f), new Color(0.2f, 0.6f, 0.25f));
                _bolt = Models.Make(new Color(0.75f, 0.78f, 0.82f));
                _zap = Models.Make(new Color(0.55f, 0.85f, 1f), new Color(0.4f, 0.8f, 1f));
                _fire = Models.Make(new Color(1f, 0.45f, 0.1f), new Color(1f, 0.4f, 0.05f));
                _heal = Models.Make(new Color(0.45f, 1f, 0.5f), new Color(0.3f, 1f, 0.4f));
                _dust = Models.Make(new Color(0.62f, 0.5f, 0.36f));
                _water = Models.Make(new Color(0.55f, 0.8f, 1f), new Color(0.4f, 0.7f, 1f));
            }
            if (e.PieceId != 0 && e.Kind != FxKind.Scoop) AimTurret(e.PieceId, e.To); // for a scoop it's the body being netted
            switch (e.Kind)
            {
                case FxKind.Tracer: StartCoroutine(Tracer(e.From, e.To)); SoundRandom("SMGFireOutside_V", 1, 4, e.From, 0.35f); break;
                case FxKind.Cannonball: StartCoroutine(Arc(e.From, e.To, _ball, 0.35f, 0.35f, 1.5f, true)); SoundRandom("ShotgunFireOutside_0", 1, 4, e.From, 0.8f); break;
                case FxKind.Spit: StartCoroutine(Arc(e.From, e.To, _spit, 0.3f, SpitFlight, 2.5f, false)); SoundRandom("PiranhaVomit_V", 1, 3, e.From, 0.8f); break;
                case FxKind.Blast: StartCoroutine(Blast(e.To, 3.5f)); Sound("Explosion", e.To, 1f, AudioDistance.Long); break;
                case FxKind.Harpoon: StartCoroutine(Bolt(e.From, e.To)); SoundRandom("BowShoot_0", 1, 4, e.From, 0.9f); break;
                case FxKind.Mortar: StartCoroutine(Arc(e.From, e.To, _ball, 0.45f, MortarFlight, 9f, true)); Sound("BoatFire", e.From, 0.8f); break;
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
                case FxKind.Wave: StartCoroutine(Blast(e.To, 6f, _water)); SoundRandom("WhaleLanding_0", 1, 3, e.To, 1f, AudioDistance.Long); break;
                case FxKind.Decoy: StartCoroutine(Duck(e.To, e.Arg)); break;
                case FxKind.Proc: GearCards.Instance.Proc((Gear)e.Arg, e.To); break;
                case FxKind.Snipe: StartCoroutine(Tracer(e.From, e.To)); SoundRandom("SniperrifleFireOutside_V", 1, 4, e.From, 0.9f, AudioDistance.Long); break;
                case FxKind.Flame:
                    for (int i = ClientSettings.Reduced ? 3 : 1; i <= 3; i++) StartCoroutine(Blast(Vector3.Lerp(e.From, e.To, i / 3f), 0.6f + 0.35f * i, _fire));
                    if (Random.value < 0.3f) Sound("Ignite", e.From, 0.5f);
                    break;
                case FxKind.Victory: StartCoroutine(Fireworks(e.To)); Reveal.Instance.Victory(e.To - Vector3.up * 6f); break;
                case FxKind.Crash:
                    try { ParticleManager.Play("BigExplosion", e.To, Vector3.up); } catch { }
                    Sound("Explosion", e.To, 1f, AudioDistance.Long);
                    SoundRandom("WhaleLanding_0", 1, 3, e.To, 1f, AudioDistance.Long);
                    break;
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

        private IEnumerator Tracer(Vector3 from, Vector3 to)
        {
            var go = Shape(PrimitiveType.Cube, _tracer);
            Vector3 d = to - from;
            go.transform.SetPositionAndRotation(from + d * 0.5f, Quaternion.LookRotation(d));
            go.transform.localScale = new Vector3(0.05f, 0.05f, d.magnitude);
            yield return new WaitForSeconds(0.05f);
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

        // Harpoon: a thin bolt flying the whole line.
        private IEnumerator Bolt(Vector3 from, Vector3 to)
        {
            var go = Shape(PrimitiveType.Cube, _bolt);
            go.transform.localScale = new Vector3(0.08f, 0.08f, 1.4f);
            go.transform.rotation = Quaternion.LookRotation(to - from);
            const float flight = 0.3f;
            for (float t = 0f; t < flight; t += Time.deltaTime) { go.transform.position = Vector3.Lerp(from, to, t / flight); yield return null; }
            Done(go);
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

        private IEnumerator Arc(Vector3 from, Vector3 to, Material mat, float size, float flight, float height, bool blastAtEnd)
        {
            var ball = Shape(PrimitiveType.Sphere, mat);
            ball.transform.localScale = Vector3.one * size;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                float k = t / flight;
                ball.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * height;
                yield return null;
            }
            Done(ball);
            if (blastAtEnd) { Sound("Explosion", to, 0.6f); yield return Blast(to, 2.5f); }
            else { SoundRandom("ItemHitWaterMedium_V", 1, 3, to, 0.8f); yield return Blast(to, 1.2f, _spit); }
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
