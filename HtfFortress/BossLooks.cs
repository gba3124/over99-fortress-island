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
            // The second form is a different creature to look at, not just a faster one: each boss gets its own ramp for
            // it (BOSS-FORMS.md). Repainting happens on every screen when the change plays (BossFx "secondform").
            switch (k)
            {
                case EnemyKind.Warden:
                    return second
                        ? (new[] { new Color(0.05f, 0.02f, 0.01f), new Color(0.5f, 0.06f, 0.02f), new Color(1f, 0.45f, 0.05f), new Color(1f, 0.95f, 0.75f) }, 0f) // molten iron
                        : (new[] { new Color(0.22f, 0.07f, 0.04f), new Color(0.7f, 0.2f, 0.07f), new Color(0.95f, 0.5f, 0.15f), new Color(1f, 0.85f, 0.45f) }, 0f);
                case EnemyKind.Urchin:
                    return second
                        ? (new[] { new Color(0.05f, 0f, 0.08f), new Color(0.35f, 0.02f, 0.5f), new Color(1f, 0.15f, 0.9f), new Color(1f, 0.9f, 1f) }, 0f) // void neon
                        : (new[] { new Color(0.12f, 0.02f, 0.18f), new Color(0.45f, 0.06f, 0.55f), new Color(0.85f, 0.2f, 0.75f), new Color(1f, 0.6f, 0.95f) }, 0f);
                case EnemyKind.Angler:
                    return second
                        ? (new[] { new Color(0.01f, 0.01f, 0.03f), new Color(0.02f, 0.1f, 0.2f), new Color(0.2f, 0.9f, 1f), new Color(0.9f, 1f, 1f) }, 0f) // abyss light
                        : (new[] { new Color(0.04f, 0.05f, 0.14f), new Color(0.1f, 0.18f, 0.38f), new Color(0.2f, 0.55f, 0.75f), new Color(0.5f, 1f, 1f) }, 0f);
                case EnemyKind.Steed:
                    return second
                        ? (new[] { new Color(0.15f, 0.08f, 0.02f), new Color(0.6f, 0.35f, 0.05f), new Color(1f, 0.8f, 0.2f), new Color(1f, 1f, 0.85f) }, 0f) // burnished gold
                        : (new[] { new Color(0.45f, 0.47f, 0.55f), new Color(0.9f, 0.92f, 0.98f), new Color(1f, 0.86f, 0.45f) }, 0f);
                case EnemyKind.Stonefish:
                    return second
                        ? (new[] { new Color(0.02f, 0.05f, 0.02f), new Color(0.15f, 0.4f, 0.05f), new Color(0.5f, 1f, 0.2f), new Color(0.9f, 1f, 0.75f) }, 0f) // toxic bloom
                        : (new[] { new Color(0.14f, 0.15f, 0.13f), new Color(0.33f, 0.36f, 0.28f), new Color(0.45f, 0.6f, 0.3f), new Color(0.65f, 1f, 0.3f) }, 0f);
                case EnemyKind.Skyfish: case EnemyKind.Squadron:
                    return second
                        ? (new[] { new Color(0.02f, 0.04f, 0.1f), new Color(0.2f, 0.45f, 0.9f), new Color(0.7f, 0.95f, 1f), new Color(1f, 1f, 1f) }, 0f) // stormhead
                        : (new[] { new Color(0.08f, 0.12f, 0.24f), new Color(0.32f, 0.48f, 0.75f), new Color(0.95f, 0.96f, 1f) }, 0.2f);
                case EnemyKind.Kraken:
                    return second
                        ? (new[] { new Color(0.02f, 0f, 0.02f), new Color(0.4f, 0f, 0.06f), new Color(0.9f, 0.1f, 0.15f), new Color(1f, 0.75f, 0.7f) }, 0f) // black blood
                        : (new[] { new Color(0.25f, 0.02f, 0.06f), new Color(0.65f, 0.05f, 0.12f), new Color(0.95f, 0.25f, 0.25f), new Color(1f, 0.7f, 0.6f) }, 0f);
                case EnemyKind.Voxel: case EnemyKind.Glitch:
                    return second
                        ? (new[] { new Color(0.2f, 0f, 0.3f), new Color(0.85f, 0.05f, 0.9f), new Color(0.1f, 1f, 1f), new Color(1f, 1f, 1f) }, 0f) // glitch: magenta/cyan
                        : (new[] { new Color(0.03f, 0.12f, 0.08f), new Color(0.15f, 0.6f, 0.35f), new Color(0.55f, 1f, 0.75f) }, 0f);
                case EnemyKind.Koi:
                    return second
                        ? (new[] { new Color(0.5f, 0.02f, 0f), new Color(1f, 0.55f, 0.05f), new Color(1f, 0.9f, 0.4f), new Color(1f, 1f, 1f) }, 0f) // dragon gold
                        : (new[] { new Color(0.62f, 0.08f, 0.05f), new Color(0.97f, 0.94f, 0.9f), new Color(1f, 0.84f, 0.4f) }, 0.25f);
                case EnemyKind.Eye:
                    return second
                        ? (new[] { new Color(0.02f, 0f, 0.02f), new Color(0.3f, 0f, 0.05f), new Color(1f, 0.1f, 0.1f), new Color(1f, 0.92f, 0.9f) }, 0f) // white-hot judgement
                        : (new[] { new Color(0.03f, 0.02f, 0.04f), new Color(0.12f, 0.06f, 0.14f), new Color(0.35f, 0.08f, 0.2f), new Color(1f, 0.15f, 0.2f) }, 0f);
                default: return (null, 1f);
            }
        }

        // The change, called from BossFx on every screen: the body is repainted for the second form and BossIk/BossBody are
        // told so (harder posing, faster sway). Safe to call again: components are added only once.
        public static void Recolor(Transform root, EnemyKind kind, bool second)
        {
            if (!root) return;
            Repaint(root, kind, second);
            var body = root.GetComponent<BossBody>();
            if (body) body.Second = second;
        }

        // The look goes on when the boss's tag arrives (Enemies.TryDress); the second form swaps the paint (the koi's dragon).
        public static void Dress(Transform root, EnemyKind kind, bool second = false)
        {
            Repaint(root, kind, second);
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true; // fins and tails: moved by BossBody, not flopping
            var body = root.GetComponent<BossBody>() ?? root.gameObject.AddComponent<BossBody>();
            body.Kind = kind;
            body.Second = second;
            // Procedural posing (head look, claw and tentacle IK) lives in BossIk; it disables itself if the body has no
            // bones we can drive, so the game's own fish still work untouched.
            if (!root.GetComponent<BossIk>()) root.gameObject.AddComponent<BossIk>().Kind = kind;
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
        // Its second form: posing is harder, the sway quicker, the aura thicker (set by BossLooks.Dress/Recolor).
        public bool Second;
        private readonly List<(Transform bone, Quaternion rest, Vector3 axis, float phase, float lag)> _swing = new List<(Transform, Quaternion, Vector3, float, float)>();
        private Vector3 _last;
        private float _speed, _nextAura, _t, _born;
        private BossSteed _steed;

        public static BossBody Of(int id) => BossFx.BodyOf(id) is Transform t && t ? t.GetComponent<BossBody>() : null;

        private void Start()
        {
            _last = transform.position;
            _born = Time.time;
            _me = GetComponent<Creature>();
            _grounded = Kind == EnemyKind.Warden || Kind == EnemyKind.Urchin || Kind == EnemyKind.Steed || Kind == EnemyKind.Stonefish;
            if (Kind == EnemyKind.Warden) (_step, _steps, _stride, _stepVol) = ("SpidercrabWalk_V", 8, 2.2f, 0.8f);
            if (Kind == EnemyKind.Stonefish) (_step, _steps, _stride, _stepVol) = ("Footstep_Sand_V", 5, 1.6f, 0.7f);
            if (Kind == EnemyKind.Steed) (_step, _steps, _stride, _stepVol) = ("Footstep_Sand_V", 5, 3f, 0.8f);
            _steed = GetComponent<BossSteed>();
            if (_steed || GetComponent<BossEye>()) return; // a body of our own over the game's: it moves itself
            int i = 0;
            foreach (var j in GetComponentsInChildren<HingeJoint>())
                _swing.Add((j.transform, j.transform.localRotation, j.axis, i * 0.8f, Kind == EnemyKind.Urchin ? 0f : 0.5f + 0.25f * i++));
            if (_swing.Count == 0)
                foreach (var sk in GetComponentsInChildren<SkinnedMeshRenderer>())
                    foreach (var b in sk.bones.Where(b => b && b.name.StartsWith("leg")))
                        _swing.Add((b, b.localRotation, Vector3.forward, i++ * Mathf.PI * 0.5f, 0f));
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime, now = Time.time;
            if (dt <= 0f) return;
            Vector3 step = transform.position - _last;
            _last = transform.position;
            bool jumped = step.sqrMagnitude > 64f; // a blink, a dive under the sea: not a move
            if (!jumped) _speed = Mathf.Lerp(_speed, step.magnitude / dt, 1f - Mathf.Exp(-4f * dt));
            if (!Frozen) _t += dt * (2f + Mathf.Min(10f, _speed * 1.2f)) * (Second ? 1.25f : 1f);
            float amp = Mathf.Lerp(12f, 28f, Mathf.Clamp01(_speed / 6f)) * (Second ? 1.35f : 1f);
            // Fins and tails trail its turns: swung the other way as it turns, and after it as it stops.
            Vector3 fwd = transform.forward.Flat();
            float yaw = fwd.sqrMagnitude > 1e-4f ? Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg : _yaw;
            _yawRate = jumped ? 0f : Mathf.Lerp(_yawRate, Mathf.DeltaAngle(_yaw, yaw) / dt, 1f - Mathf.Exp(-10f * dt));
            _yaw = yaw;
            float trail = Mathf.Clamp(-_yawRate * 0.06f, -20f, 20f);
            foreach (var (bone, rest, axis, phase, lag) in _swing)
                if (bone) bone.localRotation = (lag > 0f && bone.parent ? Quaternion.AngleAxis(trail * lag, bone.parent.InverseTransformDirection(Vector3.up)) : Quaternion.identity) * rest * Quaternion.AngleAxis(Mathf.Sin(_t + phase) * amp, axis);
            if (Time.time >= _nextAura)
            {
                _nextAura = Time.time + (Second ? 0.08f : 0.12f);
                Aura();
            }
            if (!_measured) { if (now - _born >= 0.6f) Measure(); return; }
            Feel(Mathf.Min(dt, 0.05f), step, jumped, now);
        }

        // ---------- how it carries itself (Owen: 「要感覺王不笨重 要靈活」) ----------
        // The network moves its root; its parts (the root's children) are posed round a pivot on top of that: squashed as
        // it lands and stretched while it rises or drops fast (a spring: it wobbles back), leaned into its turns and its
        // speeding up, knocked back and flashed white when it's hit, held still a beat when its move is broken. Its feet
        // thud as it walks and it lands with a thump. Visual only, on every screen.
        private sealed class Part { public Transform T; public Vector3 Pos, Scale, PosedPos, PosedScale; public Quaternion Rot, PosedRot; public bool Posed; }
        private readonly List<Part> _parts = new List<Part>();
        private Renderer[] _skin = new Renderer[0];
        private Creature _me;
        private bool _grounded, _measured, _flashing;
        private Vector3 _center, _vel, _acc, _hitAxis = Vector3.right;
        private float _half, _size = 1f, _yaw, _yawRate, _squash, _squashVel, _fall, _hitTilt, _hitVel, _hitSum, _hitAt = -9f, _flashUntil, _freezeUntil, _top, _walked;
        private int _hp = int.MinValue;
        private string _step;
        private int _steps;
        private float _stride, _stepVol;
        private const float W = 16f, Damp = 0.32f, HitW = 30f;
        private static MaterialPropertyBlock _white;

        public bool Frozen => Time.time < _freezeUntil;

        // Its move was broken: it hangs there a beat, white, then it's knocked loose (BossFx "broken").
        public void Stagger()
        {
            _freezeUntil = Time.time + 0.14f;
            Flash(0.14f);
            _hitVel += 420f;
            _squashVel -= 0.12f * W;
        }

        // Its parts as they were before they were posed (the host measures the body standing: Bosses.Stand). Only what is
        // still as we left it goes back: anything else has been moved since, and that stands.
        public void Unpose()
        {
            foreach (var part in _parts)
            {
                if (!part.T || !part.Posed) continue;
                var t = part.T;
                if (t.localPosition == part.PosedPos && t.localRotation == part.PosedRot && t.localScale == part.PosedScale)
                { t.localPosition = part.Pos; t.localRotation = part.Rot; t.localScale = part.Scale; }
                part.Posed = false;
            }
        }

        private void OnDisable()
        {
            Unpose();
            if (_flashing) Unflash();
        }

        // Once it has stood a moment (a skinned mesh's bounds only catch up when it's drawn): its parts, its skin, and its
        // middle and half its height in its own space.
        private void Measure()
        {
            _measured = true;
            foreach (Transform c in transform) if (!c.GetComponent<BossGear>()) _parts.Add(new Part { T = c });
            _skin = GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !r.forceRenderingOff && !r.GetComponentInParent<BossGear>()).ToArray();
            var b = BossLooks.WorldBounds(transform);
            _center = transform.InverseTransformPoint(b.center);
            _half = b.extents.y;
            _size = Mathf.Max(1f, b.extents.x, b.extents.z);
            _last = transform.position;
        }

        private void Feel(float dt, Vector3 step, bool jumped, float now)
        {
            if (jumped) { _vel = _acc = Vector3.zero; _fall = 0f; }
            else
            {
                Vector3 v = Vector3.Lerp(_vel, Vector3.ClampMagnitude(step / dt, 40f), 1f - Mathf.Exp(-12f * dt));
                _acc = Vector3.Lerp(_acc, (v - _vel) / dt, 1f - Mathf.Exp(-8f * dt));
                _vel = v;
            }
            Vector3 mid = transform.TransformPoint(_center), foot = mid + Vector3.down * _half;
            float above = foot.y - BossFx.Ground(foot);
            // A landing: how fast it was coming down, the moment it stops on the ground.
            if (_vel.y < -1f) _fall = Mathf.Max(_fall, -_vel.y);
            else if (_fall > 0f)
            {
                if (_fall > 3.5f && above > -1f && above < 1.2f) Land(foot, _fall);
                _fall = 0f;
            }
            if (_step != null && !jumped && above < 0.6f && (_walked += step.Flat().magnitude) >= _stride)
            {
                _walked = 0f;
                Fx.SoundRandom(_step, 1, _steps, foot, _stepVol);
            }
            Hurt(now, dt);
            if (!Frozen)
            {
                float stretch = Mathf.Clamp(Mathf.Abs(_vel.y) * 0.01f, 0f, 0.14f) * (Kind == EnemyKind.Eye ? 0.5f : 1f);
                _squashVel += (W * W * (stretch - _squash) - 2f * Damp * W * _squashVel) * dt;
                _squash = Mathf.Clamp(_squash + _squashVel * dt, -0.3f, 0.3f);
                _hitVel += (-HitW * HitW * _hitTilt - 2f * 0.4f * HitW * _hitVel) * dt;
                _hitTilt = Mathf.Clamp(_hitTilt + _hitVel * dt, -12f, 12f);
            }
            if (_flashing && now >= _flashUntil) Unflash();
            Pose(_grounded ? foot : mid);
        }

        // Down with a thump: squashed, dust, the ground shaking under it for whoever is near.
        private void Land(Vector3 at, float impact)
        {
            if (Kind == EnemyKind.Eye || Kind == EnemyKind.Voxel || Kind == EnemyKind.Glitch || Kind == EnemyKind.Kraken || Kind == EnemyKind.Squadron) return; // they hover, or float in the surf
            _squashVel -= Mathf.Min(0.22f, impact * 0.016f) * W;
            float k = Mathf.Clamp01((impact - 3.5f) / 12f);
            Fx.Sound(impact >= 8f ? "DeadPlayerImpactHard" : "DeadPlayerImpactMedium", at, Mathf.Lerp(0.5f, 0.9f, k), AudioDistance.Long);
            if (impact >= 14f) Fx.SoundRandom("LavaWhaleLanding_0", 1, 3, at, 0.7f, AudioDistance.Long);
            BossVfx.Dust(at, _size * 0.8f, 3 + Mathf.RoundToInt(5f * k));
            BossMoves.Shake(at, Mathf.Lerp(0.08f, 0.45f, k));
        }

        // Hit (its health, as every screen sees it, going down): a white flash and a knock back from whoever is looking,
        // bigger for a bigger hit. A stream of small ones adds up (and fades): it flashes as it adds up, never strobing.
        private void Hurt(float now, float dt)
        {
            if (!_me) return;
            int hp = _me._hp.Value;
            if (_hp == int.MinValue || hp >= _hp) { _hp = hp; _top = Mathf.Max(_top, hp); return; }
            _hitSum = _hitSum * Mathf.Exp(-2.8f * dt) + (_hp - hp);
            _hp = hp;
            if (now - _hitAt < 0.15f || _hitSum < Mathf.Max(1f, _top * 0.005f)) return;
            float k = Mathf.Clamp01(_hitSum / Mathf.Max(1f, _top * 0.03f));
            _hitSum = 0f;
            _hitAt = now;
            Flash(0.05f + 0.04f * k);
            var cam = GameInfo.CurCamera;
            Vector3 away = cam ? (transform.position - cam.transform.position).Flat() : -transform.forward.Flat();
            if (away.sqrMagnitude > 1e-4f) _hitAxis = Vector3.Cross(Vector3.up, away.normalized);
            _hitVel += 60f + 200f * k;
        }

        private void Flash(float seconds)
        {
            _flashUntil = Mathf.Max(_flashUntil, Time.time + seconds);
            if (_flashing) return;
            if (_white == null)
            {
                _white = new MaterialPropertyBlock();
                _white.SetTexture("_Colors", Texture2D.whiteTexture);
                _white.SetFloat("_Emission", 0.6f);
            }
            foreach (var r in _skin) if (r) r.SetPropertyBlock(_white);
            _flashing = true;
        }
        private void Unflash()
        {
            foreach (var r in _skin) if (r) r.SetPropertyBlock(null);
            _flashing = false;
        }

        // Its parts squashed toward `pivot` (along the world's up, keeping its bulk) and leaned: nose down as it speeds up
        // (up as it brakes), banked into its turns (a flyer harder), tipped back where it's hit. Worked out in the root's
        // own space, each part from where it was before it was posed (or where something else has moved it since).
        private void Pose(Vector3 pivot)
        {
            Vector3 fwd = transform.forward.Flat();
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float m = Kind == EnemyKind.Urchin ? 0f : Kind == EnemyKind.Eye ? 0.4f : Kind == EnemyKind.Kraken ? 0.5f : 1f;
            bool flyer = Kind == EnemyKind.Skyfish || Kind == EnemyKind.Squadron || Kind == EnemyKind.Koi;
            float pitch = Mathf.Clamp(Vector3.Dot(_acc, fwd) * 0.5f, -7f, 7f) * m, roll = -Mathf.Clamp(Vector3.Dot(_acc, right) * 0.6f, -10f, 10f) * m * (flyer ? 1.4f : 1f);
            Quaternion lean = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(roll, fwd) * Quaternion.AngleAxis(_hitTilt, _hitAxis);
            Quaternion inv = Quaternion.Inverse(transform.rotation), l = inv * lean * transform.rotation;
            Vector3 pv = transform.InverseTransformPoint(pivot), u = inv * Vector3.up;
            float sy = 1f + _squash, sx = 1f / Mathf.Sqrt(Mathf.Max(0.5f, sy));
            foreach (var part in _parts)
            {
                var t = part.T;
                if (!t) continue;
                if (!part.Posed || t.localPosition != part.PosedPos || t.localRotation != part.PosedRot || t.localScale != part.PosedScale)
                { part.Pos = t.localPosition; part.Rot = t.localRotation; part.Scale = t.localScale; }
                Vector3 d = part.Pos - pv;
                part.PosedPos = pv + l * (sx * d + (sy - sx) * Vector3.Dot(d, u) * u);
                part.PosedRot = l * part.Rot;
                part.PosedScale = Vector3.Scale(part.Scale, new Vector3(Stretch(part.Rot * Vector3.right, u, sx, sy), Stretch(part.Rot * Vector3.up, u, sx, sy), Stretch(part.Rot * Vector3.forward, u, sx, sy)));
                t.localPosition = part.PosedPos;
                t.localRotation = part.PosedRot;
                t.localScale = part.PosedScale;
                part.PosedRot = t.localRotation; // (as it was stored)
                part.Posed = true;
            }
        }

        // How much longer a part's axis `a` gets, squashed by `sy` along `u` and `sx` across.
        private static float Stretch(Vector3 a, Vector3 u, float sx, float sy)
        {
            float du = Vector3.Dot(a, u);
            return Mathf.Sqrt(sx * sx + (sy * sy - sx * sx) * du * du);
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
