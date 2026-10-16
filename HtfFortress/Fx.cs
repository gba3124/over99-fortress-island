using System.Collections;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using UnityEngine;

namespace HtfFortress
{
    public enum FxKind : byte { Tracer, Cannonball, Blast, Spit, Announce, Harpoon, Mortar, Zap, Burn, Scoop, Sell, Heal, Dust, Wave }

    // Host -> everyone: a visual effect (and for Announce, a banner: Key = string key, Arg = number).
    public struct FxEvent : IBroadcast { public FxKind Kind; public Vector3 From; public Vector3 To; public int PieceId; public string Key; public int Arg; }

    // All transient visuals in one place, drawn on every client from host broadcasts.
    public class Fx : MonoBehaviour
    {
        private static Fx _instance;
        private NetworkManager _registeredOn;
        private Material _tracer, _ball, _blast, _spit, _bolt, _zap, _fire, _heal, _dust, _water;

        static Fx()
        {
            GenericWriter<FxEvent>.SetWrite((w, v) => { w.WriteByte((byte)v.Kind); w.WriteVector3(v.From); w.WriteVector3(v.To); w.WriteInt32(v.PieceId); w.WriteString(v.Key ?? ""); w.WriteInt32(v.Arg); });
            GenericReader<FxEvent>.SetRead(r => new FxEvent { Kind = (FxKind)r.ReadByte(), From = r.ReadVector3(), To = r.ReadVector3(), PieceId = r.ReadInt32(), Key = r.ReadString(), Arg = r.ReadInt32() });
        }

        private void Awake() => _instance = this;

        // Host: send to everyone (the host's own client receives it too).
        // `damage` > 0 shows the game's own damage number where it lands.
        public static void Send(FxKind kind, Vector3 from, Vector3 to, int pieceId = 0, int damage = 0)
        {
            if (InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Broadcast(new FxEvent { Kind = kind, From = from, To = to, PieceId = pieceId, Arg = damage });
        }

        // The game's own sounds (see BepInEx/fortress-sounds.txt for every name).
        public static void Sound(string clip, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium)
        {
            try { AudioManager.PlayClipAt(clip, at, true, dist, volume); } catch { }
        }

        public static void SoundRandom(string prefix, int min, int max, Vector3 at, float volume = 1f, AudioDistance dist = AudioDistance.Medium)
        {
            try { AudioManager.PlayRandomClipAt(prefix, min, max, at, true, dist, volume); } catch { }
        }

        public static void Announce(string key, int arg = 0)
        {
            if (InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Broadcast(new FxEvent { Kind = FxKind.Announce, Key = key, Arg = arg });
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ClientManager.RegisterBroadcast<FxEvent>((e, ch) => Play(e));
            }
        }

        private void Play(FxEvent e)
        {
            if (e.Kind == FxKind.Announce)
            {
                if (FortressIsland.Instance) FortressIsland.Instance.Banner(L10n.T(e.Key, e.Arg), 3.5f);
                Vector3 me = Player.LocalPlayer && Player.LocalPlayer.Transform ? Player.LocalPlayer.Transform.position : Vector3.zero;
                switch (e.Key)
                {
                    case "bossBannerBoss": case "bossBannerQueen": case "bossBannerTide": Sound("BossFightIntro", me, 1f, AudioDistance.Long); Sound("WhaleNoise_03", me, 1f, AudioDistance.Long); break;
                    case "clearedBanner": Sound("WinRound", me, 0.8f, AudioDistance.Long); break;
                    default: Sound("QuestReceived", me, 0.9f, AudioDistance.Long); break;
                }
                return;
            }
            if (e.Arg > 0) { try { PlayerUI.AddDamageNumber(e.To + Vector3.up * 0.8f, e.Arg, false); } catch { } }
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
            if (e.PieceId != 0) AimTurret(e.PieceId, e.To);
            switch (e.Kind)
            {
                case FxKind.Tracer: StartCoroutine(Tracer(e.From, e.To)); SoundRandom("SMGFireOutside_V", 1, 4, e.From, 0.35f); break;
                case FxKind.Cannonball: StartCoroutine(Arc(e.From, e.To, _ball, 0.35f, 0.35f, 1.5f, true)); SoundRandom("ShotgunFireOutside_0", 1, 4, e.From, 0.8f); break;
                case FxKind.Spit: StartCoroutine(Arc(e.From, e.To, _spit, 0.3f, SpitFlight, 2.5f, false)); SoundRandom("PiranhaVomit_V", 1, 3, e.From, 0.8f); break;
                case FxKind.Blast: StartCoroutine(Blast(e.To, 3.5f)); Sound("Explosion", e.To, 1f, AudioDistance.Long); break;
                case FxKind.Harpoon: StartCoroutine(Bolt(e.From, e.To)); SoundRandom("BowShoot_0", 1, 4, e.From, 0.9f); break;
                case FxKind.Mortar: StartCoroutine(Arc(e.From, e.To, _ball, 0.45f, MortarFlight, 9f, true)); Sound("BoatFire", e.From, 0.8f); break;
                case FxKind.Zap: StartCoroutine(Zap(e.From, e.To)); if (e.PieceId != 0) Sound("Ignite", e.From, 0.6f); break;
                case FxKind.Scoop: StartCoroutine(Fly(e.From, e.To + Vector3.up * 1.2f)); SoundRandom("PickUp_V", 1, 3, e.To, 0.7f, AudioDistance.Short); break;
                case FxKind.Sell: SoundRandom("Sell_0", 1, 3, e.To, 1f); StartCoroutine(Blast(e.To + Vector3.up, 1.6f, _tracer)); break;
                case FxKind.Burn: StartCoroutine(Blast(e.To + Random.insideUnitSphere * 1.5f, 1.4f, _fire)); break;
                case FxKind.Heal: StartCoroutine(Zap(e.From, e.To, _heal)); StartCoroutine(Blast(e.To, 0.9f, _heal)); Sound("QuestReceived", e.To, 0.25f, AudioDistance.Short); break;
                case FxKind.Dust: StartCoroutine(Blast(e.To, 2.2f, _dust)); SoundRandom("ItemImpactHeavy_V", 1, 3, e.To, 0.8f); break;
                case FxKind.Wave: StartCoroutine(Blast(e.To, 6f, _water)); SoundRandom("WhaleLanding_0", 1, 3, e.To, 1f, AudioDistance.Long); break;
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

        private static GameObject Shape(PrimitiveType type, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private IEnumerator Tracer(Vector3 from, Vector3 to)
        {
            var go = Shape(PrimitiveType.Cube, _tracer);
            Vector3 d = to - from;
            go.transform.SetPositionAndRotation(from + d * 0.5f, Quaternion.LookRotation(d));
            go.transform.localScale = new Vector3(0.05f, 0.05f, d.magnitude);
            yield return new WaitForSeconds(0.05f);
            Destroy(go);
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
            Destroy(go);
        }

        // Harpoon: a thin bolt flying the whole line.
        private IEnumerator Bolt(Vector3 from, Vector3 to)
        {
            var go = Shape(PrimitiveType.Cube, _bolt);
            go.transform.localScale = new Vector3(0.08f, 0.08f, 1.4f);
            go.transform.rotation = Quaternion.LookRotation(to - from);
            const float flight = 0.3f;
            for (float t = 0f; t < flight; t += Time.deltaTime) { go.transform.position = Vector3.Lerp(from, to, t / flight); yield return null; }
            Destroy(go);
        }

        // Tesla: a jagged bright line that flickers out.
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
            foreach (var go in parts) Destroy(go);
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
            Destroy(ball);
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
            Destroy(blast);
        }
    }
}
