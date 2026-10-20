using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // How a boss looks, on every client: its body (one of the game's creatures) blown up, repainted (its palette run through
    // the boss's own colour ramp), with a glow and an aura in its colour, its fins and legs moved by hand (the game's
    // physics that flaps them is off: the body is ours).
    public static class BossLooks
    {
        // The creature each boss is made of, how big it is (times the creature), and which way its head points.
        public static string BodyOf(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Warden: return "lobster";
                case EnemyKind.Urchin: return "seaurchin";
                case EnemyKind.Angler: return "anglerfish";
                case EnemyKind.Steed: return "seahorse";
                case EnemyKind.Stonefish: return "stonefish";
                case EnemyKind.Skyfish: case EnemyKind.Squadron: return "flyingfish";
                case EnemyKind.Kraken: return "squid";
                case EnemyKind.Voxel: case EnemyKind.Glitch: return "voxelfish";
                case EnemyKind.Koi: return "koifish";
                case EnemyKind.Eye: return "footsnail"; // hidden under a body of our own (BossEye.cs)
                default: return null;
            }
        }

        // How the body is turned from "head down +z, back up" (the game's fish are modelled that way; a few aren't).
        public static Quaternion Turn(EnemyKind k) => Quaternion.Euler(Pitch(k), Yaw(k), 0f);
        public static float Yaw(EnemyKind k) => 0f;
        public static float Pitch(EnemyKind k) => 0f; // (the squid stood upright drew apart from its bounds: left lying on the sea)

        // A body's box in the world, from its meshes (a skinned mesh's own bounds only catch up when it's next drawn: right
        // after a spawn and a resize they're the old ones).
        public static Bounds WorldBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds(root.position, Vector3.zero);
            void Add(Matrix4x4 m, Bounds local)
            {
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r.forceRenderingOff || r is ParticleSystemRenderer || r is LineRenderer || r.GetComponentInParent<BossGear>()) continue; // (hidden: a body under ours)
                if (r is SkinnedMeshRenderer sk) Add((sk.rootBone ? sk.rootBone : sk.transform).localToWorldMatrix, sk.localBounds);
                else if (r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh) Add(r.transform.localToWorldMatrix, mf.sharedMesh.bounds);
            }
            return any ? b : new Bounds(root.position, Vector3.one * 2f);
        }

        // A body of our own over the game's creature (BossEye, BossSteed): the creature's box, what the game's own guns hit,
        // fitted round our meshes under `body` instead (not its gear).
        public static void FitCollider(Transform root, Transform body)
        {
            var box = root.GetComponentsInChildren<BoxCollider>().FirstOrDefault();
            if (!box) return;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in body.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponentInParent<BossGear>() || !mf.sharedMesh) continue;
                var toBox = box.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var m = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = toBox.MultiplyPoint3x4(m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (any) { box.center = b.center; box.size = b.size; }
        }

        public static Color Colour(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Warden: return new Color(1f, 0.55f, 0.2f);
                case EnemyKind.Urchin: return new Color(1f, 0.3f, 0.85f);
                case EnemyKind.Angler: return new Color(0.3f, 0.9f, 1f);
                case EnemyKind.Steed: return new Color(1f, 0.85f, 0.4f);
                case EnemyKind.Stonefish: return new Color(0.55f, 1f, 0.3f);
                case EnemyKind.Skyfish: case EnemyKind.Squadron: return new Color(0.45f, 0.7f, 1f);
                case EnemyKind.Kraken: return new Color(0.95f, 0.15f, 0.3f);
                case EnemyKind.Voxel: case EnemyKind.Glitch: return new Color(0.4f, 1f, 0.6f);
                case EnemyKind.Koi: return new Color(1f, 0.6f, 0.15f);
                case EnemyKind.Eye: return new Color(1f, 0.08f, 0.12f);
                default: return new Color(1f, 0.3f, 0.2f);
            }
        }

        // Its palette, dark to light: a gradient the creature's own colours are mapped through (by how light they are),
        // and how much of the original colour shows through.
        private static (Color[] ramp, float keep) Paint(EnemyKind k, bool second)
        {
            switch (k)
            {
                case EnemyKind.Warden: return (new[] { new Color(0.22f, 0.07f, 0.04f), new Color(0.7f, 0.2f, 0.07f), new Color(0.95f, 0.5f, 0.15f), new Color(1f, 0.85f, 0.45f) }, 0f);
                case EnemyKind.Urchin: return (new[] { new Color(0.12f, 0.02f, 0.18f), new Color(0.45f, 0.06f, 0.55f), new Color(0.85f, 0.2f, 0.75f), new Color(1f, 0.6f, 0.95f) }, 0f);
                case EnemyKind.Angler: return (new[] { new Color(0.04f, 0.05f, 0.14f), new Color(0.1f, 0.18f, 0.38f), new Color(0.2f, 0.55f, 0.75f), new Color(0.5f, 1f, 1f) }, 0f);
                case EnemyKind.Steed: return (new[] { new Color(0.45f, 0.47f, 0.55f), new Color(0.9f, 0.92f, 0.98f), new Color(1f, 0.86f, 0.45f) }, 0f);
                case EnemyKind.Stonefish: return (new[] { new Color(0.14f, 0.15f, 0.13f), new Color(0.33f, 0.36f, 0.28f), new Color(0.45f, 0.6f, 0.3f), new Color(0.65f, 1f, 0.3f) }, 0f);
                case EnemyKind.Skyfish: case EnemyKind.Squadron: return (new[] { new Color(0.08f, 0.12f, 0.24f), new Color(0.32f, 0.48f, 0.75f), new Color(0.95f, 0.96f, 1f) }, 0.2f);
                case EnemyKind.Kraken: return (new[] { new Color(0.25f, 0.02f, 0.06f), new Color(0.65f, 0.05f, 0.12f), new Color(0.95f, 0.25f, 0.25f), new Color(1f, 0.7f, 0.6f) }, 0f);
                case EnemyKind.Koi:
                    return second ? (new[] { new Color(0.4f, 0.02f, 0.02f), new Color(0.92f, 0.5f, 0.08f), new Color(1f, 0.95f, 0.55f) }, 0f)
                                  : (new[] { new Color(0.62f, 0.08f, 0.05f), new Color(0.97f, 0.94f, 0.9f), new Color(1f, 0.84f, 0.4f) }, 0.25f);
                case EnemyKind.Eye: return (new[] { new Color(0.03f, 0.02f, 0.04f), new Color(0.12f, 0.06f, 0.14f), new Color(0.35f, 0.08f, 0.2f), new Color(1f, 0.15f, 0.2f) }, 0f);
                default: return (null, 1f);
            }
        }

        // The look goes on when the boss's tag arrives (Enemies.TryDress); the second form swaps the paint (the koi's dragon).
        public static void Dress(Transform root, EnemyKind kind, bool second = false)
        {
            Repaint(root, kind, second);
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true; // fins and tails: moved by BossBody, not flopping
            var body = root.GetComponent<BossBody>() ?? root.gameObject.AddComponent<BossBody>();
            body.Kind = kind;
            if (kind != EnemyKind.Squadron && kind != EnemyKind.Glitch && !root.Find("BossGlow"))
            {
                var glow = new GameObject("BossGlow").AddComponent<Light>();
                glow.transform.SetParent(root, false);
                glow.transform.localPosition = Vector3.up * 0.3f;
                glow.type = LightType.Point;
                glow.color = Colour(kind);
                glow.range = 9f;
                glow.intensity = 1.6f;
                glow.shadows = LightShadows.None;
            }
            if (kind == EnemyKind.Eye) BossEye.Dress(root);
            if (kind == EnemyKind.Steed) BossSteed.Dress(root);
        }

        private static readonly Dictionary<(Texture, EnemyKind, bool), Texture2D> Painted = new Dictionary<(Texture, EnemyKind, bool), Texture2D>();

        private static void Repaint(Transform root, EnemyKind kind, bool second)
        {
            var (ramp, keep) = Paint(kind, second);
            if (ramp == null) return;
            var own = root.GetComponent<Models.OwnMaterials>() ?? root.gameObject.AddComponent<Models.OwnMaterials>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.GetComponentInParent<BossGear>()) continue;
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (!own.Mats.Contains(m)) own.Mats.Add(m);
                    if (!m.HasProperty("_Colors") || !(m.GetTexture("_Colors") is Texture src)) continue;
                    if (src.name.StartsWith("Boss")) src = Source.TryGetValue(src, out var s0) ? s0 : src;
                    if (!Painted.TryGetValue((src, kind, second), out var tex)) Painted[(src, kind, second)] = tex = Ramp(src, ramp, keep, kind + (second ? "2" : ""));
                    if (tex) { m.SetTexture("_Colors", tex); Source[tex] = src; }
                    if (m.HasProperty("_Emission")) m.SetFloat("_Emission", kind == EnemyKind.Eye || (kind == EnemyKind.Koi && second) ? 0.35f : 0f);
                }
                r.materials = mats;
            }
        }
        private static readonly Dictionary<Texture, Texture> Source = new Dictionary<Texture, Texture>();

        // The palette read back from the GPU, every texel's lightness looked up in `ramp`.
        private static Texture2D Ramp(Texture src, Color[] ramp, float keep, string name)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { name = "Boss" + name, filterMode = src.filterMode, wrapMode = src.wrapMode };
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            // The creature's own darkest to lightest is stretched over the whole ramp (a dark fish gets every colour of it).
            float lo0 = 1f, hi0 = 0f;
            foreach (var c in px) { if (c.a < 0.5f) continue; float l0 = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; lo0 = Mathf.Min(lo0, l0); hi0 = Mathf.Max(hi0, l0); }
            if (hi0 - lo0 < 0.05f) { lo0 = 0f; hi0 = 1f; }
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float l = Mathf.Clamp01((0.299f * c.r + 0.587f * c.g + 0.114f * c.b - lo0) / (hi0 - lo0));
                float f = l * (ramp.Length - 1);
                int lo = Mathf.Min(ramp.Length - 2, Mathf.FloorToInt(f));
                var to = Color.Lerp(ramp[lo], ramp[lo + 1], f - lo);
                px[i] = new Color(Mathf.Lerp(to.r, c.r, keep), Mathf.Lerp(to.g, c.g, keep), Mathf.Lerp(to.b, c.b, keep), c.a);
            }
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }
    }

    // Marks things we hang on a boss (they keep their own colours when it's repainted).
    public class BossGear : MonoBehaviour { }

    // A boss's body, alive on every screen: its tail and fins sway (faster as it moves), legs step, and its aura drifts
    // off it. The body's position and turning come from the host.
    public class BossBody : MonoBehaviour
    {
        public EnemyKind Kind;
        private readonly List<(Transform bone, Quaternion rest, Vector3 axis, float phase)> _swing = new List<(Transform, Quaternion, Vector3, float)>();
        private Vector3 _last;
        private float _speed, _nextAura, _t;
        private BossSteed _steed;

        private void Start()
        {
            _last = transform.position;
            _steed = GetComponent<BossSteed>();
            if (_steed || GetComponent<BossEye>()) return; // a body of our own over the game's: it moves itself
            int i = 0;
            foreach (var j in GetComponentsInChildren<HingeJoint>())
                _swing.Add((j.transform, j.transform.localRotation, j.axis, i++ * 0.8f));
            if (_swing.Count == 0)
                foreach (var sk in GetComponentsInChildren<SkinnedMeshRenderer>())
                    foreach (var b in sk.bones.Where(b => b && b.name.StartsWith("leg")))
                        _swing.Add((b, b.localRotation, Vector3.forward, i++ * Mathf.PI * 0.5f));
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _speed = Mathf.Lerp(_speed, (transform.position - _last).magnitude / dt, 1f - Mathf.Exp(-4f * dt));
            _last = transform.position;
            _t += dt * (2f + Mathf.Min(10f, _speed * 1.2f));
            float amp = Mathf.Lerp(12f, 28f, Mathf.Clamp01(_speed / 6f));
            foreach (var (bone, rest, axis, phase) in _swing)
                if (bone) bone.localRotation = rest * Quaternion.AngleAxis(Mathf.Sin(_t + phase) * amp, axis);
            if (Time.time >= _nextAura)
            {
                _nextAura = Time.time + 0.12f;
                Aura();
            }
        }

        private void Aura()
        {
            var c = BossLooks.Colour(Kind);
            Vector3 at = _steed ? _steed.AuraPoint() : transform.position + Random.insideUnitSphere * (Kind == EnemyKind.Eye ? 4f : 1.5f * transform.lossyScale.x * 0.1f); // (the eye: round its socket, not 14 m out)
            switch (Kind)
            {
                case EnemyKind.Urchin: case EnemyKind.Eye: case EnemyKind.Koi:
                    WeaponFx.Emit(BossVfx.Sparks(c), at, 1, Vector3.up, size: 0.12f, life: 0.8f);
                    break;
                case EnemyKind.Angler: case EnemyKind.Kraken:
                    WeaponFx.Emit(WeaponFx.Bubbles, at, 1, Vector3.up);
                    break;
                case EnemyKind.Stonefish:
                    WeaponFx.Emit(BossVfx.Sparks(c), at, 1, Vector3.up, size: 0.1f, life: 0.9f);
                    break;
                case EnemyKind.Squadron: case EnemyKind.Glitch:
                    break;
                default:
                    WeaponFx.Emit(BossVfx.Sparks(c), at, 1, Vector3.up, size: 0.08f, life: 0.6f);
                    break;
            }
        }
    }
}
