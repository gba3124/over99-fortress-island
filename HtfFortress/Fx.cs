using System.Collections;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using UnityEngine;

namespace HtfFortress
{
    public enum FxKind : byte { Tracer, Cannonball, Blast, Spit, Announce }

    // Host -> everyone: a visual effect (and for Announce, a banner: Key = string key, Arg = number).
    public struct FxEvent : IBroadcast { public FxKind Kind; public Vector3 From; public Vector3 To; public int PieceId; public string Key; public int Arg; }

    // All transient visuals in one place, drawn on every client from host broadcasts.
    public class Fx : MonoBehaviour
    {
        private static Fx _instance;
        private NetworkManager _registeredOn;
        private Material _tracer, _ball, _blast, _spit;

        static Fx()
        {
            GenericWriter<FxEvent>.SetWrite((w, v) => { w.WriteByte((byte)v.Kind); w.WriteVector3(v.From); w.WriteVector3(v.To); w.WriteInt32(v.PieceId); w.WriteString(v.Key ?? ""); w.WriteInt32(v.Arg); });
            GenericReader<FxEvent>.SetRead(r => new FxEvent { Kind = (FxKind)r.ReadByte(), From = r.ReadVector3(), To = r.ReadVector3(), PieceId = r.ReadInt32(), Key = r.ReadString(), Arg = r.ReadInt32() });
        }

        private void Awake() => _instance = this;

        // Host: send to everyone (the host's own client receives it too).
        public static void Send(FxKind kind, Vector3 from, Vector3 to, int pieceId = 0)
        {
            if (InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Broadcast(new FxEvent { Kind = kind, From = from, To = to, PieceId = pieceId });
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
                return;
            }
            if (!_tracer)
            {
                _tracer = Models.Make(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.7f, 0.2f));
                _ball = Models.Make(new Color(0.15f, 0.15f, 0.15f));
                _blast = Models.Make(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.5f, 0.1f));
                _spit = Models.Make(new Color(0.35f, 0.85f, 0.4f), new Color(0.2f, 0.6f, 0.25f));
            }
            if (e.PieceId != 0) AimTurret(e.PieceId, e.To);
            switch (e.Kind)
            {
                case FxKind.Tracer: StartCoroutine(Tracer(e.From, e.To)); break;
                case FxKind.Cannonball: StartCoroutine(Arc(e.From, e.To, _ball, 0.35f, 0.35f, 1.5f, true)); break;
                case FxKind.Spit: StartCoroutine(Arc(e.From, e.To, _spit, 0.3f, SpitFlight, 2.5f, false)); break;
                case FxKind.Blast: StartCoroutine(Blast(e.To, 3.5f)); break;
            }
        }

        public const float SpitFlight = 0.8f;

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
            if (blastAtEnd) yield return Blast(to, 2.5f);
            else yield return Blast(to, 1.2f, _spit);
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
