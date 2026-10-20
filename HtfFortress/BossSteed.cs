using System.IO;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // The Silver Steed wears a body of our own (tools/make_boss_steed.py): a winged unicorn in armour (Owen: 「可以有獨角有
    // 裝備也會很好！」). It used to be the game's horse blown up and painted, its back half wagging like a fish's tail and its
    // legs never moving. Now its parts turn on their joints: it stands with its wings raised, gallops when it runs, tucks
    // its legs and beats its wings in a leap, paws the air when it rears. The brain only moves its root (and turns it up
    // about its hind hooves to rear); every screen reads the pose from that. The game's horse stays underneath, hidden, for
    // its network object, its health and its colliders, which are moved under ours.
    public class BossSteed : MonoBehaviour
    {
        public static readonly string[] Parts = { "BossSteedBody", "BossSteedHead", "BossSteedTail", "BossSteedForeArm", "BossSteedForeShin", "BossSteedThigh", "BossSteedHindShin", "BossSteedWing" };
        // Its joints where make_boss_steed.py puts them (metres; Unity axes, from the root, which is the middle of its back).
        // The left leg and wing (x < 0) as built; the right ones mirrored.
        public static readonly Vector3 Neck = new Vector3(0f, -0.2f, 2.9f), Dock = new Vector3(0f, -0.15f, -3.62f), Wing = new Vector3(-0.7f, 0.55f, 1.5f),
            Shoulder = new Vector3(-0.62f, -1.3f, 2.55f), Knee = new Vector3(0f, -2.7f, 0.07f),   // the knee from the shoulder
            Hip = new Vector3(-0.66f, -0.75f, -2.35f), Hock = new Vector3(0f, -3f, -0.65f);       // the hock from the hip
        public const float Hooves = 5.7f;      // under the root: where the game's horse has its hooves, so the brain's footing holds
        public const float HorseShift = 6.88f; // the hidden horse moved forward (m): its leg colliders under our legs
        private static readonly string[] Legs = { "FL", "FR", "HL", "HR" };

        private Transform _body, _head, _tail;
        private readonly Transform[] _upper = new Transform[4], _lower = new Transform[4], _wings = new Transform[2];
        private Renderer[] _sparkle;
        private Vector3 _last;
        private float _speed, _stride, _run, _fly, _rear;
        private readonly float[] _pose = new float[N], _other = new float[N];

        // Swaps the horse for our steed. False if the models aren't there: it stays the horse.
        public static bool Dress(Transform root)
        {
            if (root.GetComponent<BossSteed>()) return true;
            if (Parts.Any(p => !File.Exists(Path.Combine(Models.Dir ?? "", p + ".obj")))) return false;
            var horse = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToList();
            var rig = new GameObject("BossSteedRig").transform;
            rig.SetParent(root, false);
            rig.localScale = Vector3.one / root.localScale.x; // ours are in metres, the horse is blown up 4.2 times
            rig.gameObject.layer = horse.Count > 0 ? horse[0].gameObject.layer : root.gameObject.layer;
            Transform Part(string model, Transform parent, string name, Vector3 at)
            {
                var t = Models.Attach(model, parent, Vector3.one).transform;
                t.name = name;
                t.localPosition = at;
                return t;
            }
            var body = Part("BossSteedBody", rig, "Body", Vector3.zero);
            Part("BossSteedHead", body, "Head", Neck);
            Part("BossSteedTail", body, "Tail", Dock);
            for (int i = 0; i < 4; i++)
            {
                var side = new Vector3(i % 2 == 0 ? 1f : -1f, 1f, 1f);
                var leg = Part(i < 2 ? "BossSteedForeArm" : "BossSteedThigh", body, Legs[i], Vector3.Scale(i < 2 ? Shoulder : Hip, side));
                Part(i < 2 ? "BossSteedForeShin" : "BossSteedHindShin", leg, "Shin", i < 2 ? Knee : Hock);
            }
            BossLooks.FitCollider(root, rig); // the box the game's guns hit: round the horse, not its wings (they move)
            Part("BossSteedWing", body, "WingL", Wing);
            var right = Part("BossSteedWing", body, "WingR", Vector3.Scale(Wing, new Vector3(-1f, 1f, 1f))).GetComponent<MeshFilter>();
            right.sharedMesh = Mirrored(right.sharedMesh);
            foreach (var r in horse) r.forceRenderingOff = true; // not `enabled`: the game turns that on and off itself
            if (root.Find("Horse") is Transform h) h.localPosition += Vector3.forward * (HorseShift / root.localScale.z);
            if (root.Find("BossGlow") is Transform glow && glow.GetComponent<Light>() is Light light)
            {
                glow.SetParent(rig, false);
                glow.localPosition = Vector3.up * 2.5f; // over its back, between its wings
                light.range = 14f;
            }
            root.gameObject.AddComponent<BossSteed>().Bind(body);
            return true;
        }

        // The right wing: the left one's mesh in a mirror (made once).
        private static Mesh _mirrored;
        private static Mesh Mirrored(Mesh m)
        {
            if (_mirrored) return _mirrored;
            var c = Instantiate(m);
            c.name = m.name + "Right";
            c.SetVertices(m.vertices.Select(v => new Vector3(-v.x, v.y, v.z)).ToList());
            for (int i = 0; i < m.subMeshCount; i++) c.SetTriangles(m.GetTriangles(i).Reverse().ToArray(), i);
            c.RecalculateNormals();
            c.RecalculateBounds();
            return _mirrored = c;
        }

        private void Bind(Transform body)
        {
            _body = body;
            _head = body.Find("Head");
            _tail = body.Find("Tail");
            for (int i = 0; i < 4; i++) { _upper[i] = body.Find(Legs[i]); _lower[i] = _upper[i].Find("Shin"); }
            _wings[0] = body.Find("WingL");
            _wings[1] = body.Find("WingR");
            _sparkle = new[] { _wings[0], _wings[1], _head, _tail }.Select(t => t.GetComponent<Renderer>()).ToArray();
            _last = transform.position;
        }

        // A ghost of it for the phantom cavalry: a copy of its body in `mat` that gallops wherever it's moved.
        public Transform Phantom(Material mat)
        {
            var root = new GameObject("BossSteedPhantom").transform;
            root.SetPositionAndRotation(transform.position, transform.rotation);
            var body = Instantiate(_body, root, false);
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.identity;
            body.localScale = Vector3.one;
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            root.gameObject.AddComponent<BossSteed>().Bind(body);
            return root;
        }

        // Somewhere about its wings, its mane and its tail, for its aura's sparks (BossBody).
        public Vector3 AuraPoint()
        {
            var b = _sparkle[Random.Range(0, _sparkle.Length)].bounds;
            return b.center + Vector3.Scale(b.extents, new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime, now = Time.time;
            if (dt <= 0f || !_body) return;
            // How it moves: its speed over the ground, its nose turned up (a rear), its hooves off the ground (a leap).
            Vector3 p = transform.position, v = (p - _last) / dt;
            _last = p;
            _speed = Mathf.Lerp(_speed, Mathf.Min(60f, new Vector2(v.x, v.z).magnitude), 1f - Mathf.Exp(-6f * dt));
            float nose = Mathf.Asin(Mathf.Clamp(transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float air = p.y - Hooves - BossFx.Ground(p);
            Ease(ref _rear, Mathf.Clamp01(nose / 28f), 8f, dt);
            Ease(ref _fly, air > 1f && nose < 10f ? 1f : 0f, 5f, dt);
            Ease(ref _run, Mathf.Clamp01((_speed - 1.5f) / 4f), 4f, dt);
            _stride = Mathf.Repeat(_stride + dt * Mathf.Clamp(_speed / 9f, 0.8f, 2.4f), 1f);

            Rest(_pose, now);
            Gallop(_other, _stride);
            Mix(_pose, _other, _run);
            Leap(_other, now);
            Mix(_pose, _other, _fly);
            Rear(_other, now);
            Mix(_pose, _other, _rear);

            _body.localPosition = Vector3.up * _pose[Bob];
            _body.localRotation = Quaternion.AngleAxis(_pose[Pitch], Vector3.right);
            _head.localRotation = Quaternion.AngleAxis(_pose[NeckTurn], Vector3.right);
            _tail.localRotation = Quaternion.AngleAxis(_pose[TailLift], Vector3.right);
            for (int i = 0; i < 4; i++)
            {
                _upper[i].localRotation = Quaternion.AngleAxis(_pose[i], Vector3.right);
                _lower[i].localRotation = Quaternion.AngleAxis(_pose[4 + i], Vector3.right);
            }
            var w = Quaternion.AngleAxis(_pose[Tilt], Vector3.right) * Quaternion.AngleAxis(-_pose[Sweep], Vector3.up) * Quaternion.AngleAxis(_pose[Raise], Vector3.forward);
            _wings[0].localRotation = w;
            _wings[1].localRotation = new Quaternion(w.x, -w.y, -w.z, w.w); // the left one's turn in a mirror
        }

        private static void Ease(ref float v, float to, float rate, float dt) => v = Mathf.Lerp(v, to, 1f - Mathf.Exp(-rate * dt));

        // ---------- poses (the same numbers as the preview's, tools/steed_preview/pose.py) ----------
        // Each leg's upper and lower angle (front left, front right, hind left, hind right; + swings the hoof back), the
        // body's pitch (+ nose down) and bob (m), the neck (+ nose down), the tail (+ lifted back), and the wings: raised
        // (- up), swept (+ tips back), tilted (- leaning back).
        public const int Pitch = 8, Bob = 9, NeckTurn = 10, TailLift = 11, Raise = 12, Sweep = 13, Tilt = 14, N = 15;
        private static readonly float[] Stride = { 0.45f, 0.55f, 0f, 0.1f }; // where each leg is in the gallop's stride

        private static void Mix(float[] into, float[] from, float k)
        {
            for (int i = 0; i < N; i++) into[i] = Mathf.Lerp(into[i], from[i], k);
        }

        // Standing: wings raised up and back, breathing, now and then a forehoof paws the ground.
        public static void Rest(float[] o, float t)
        {
            System.Array.Clear(o, 0, N);
            float breath = Mathf.Sin(t * 1.3f), paw = Mathf.Repeat(t, 7f);
            o[Bob] = 0.04f * breath;
            o[NeckTurn] = 4f + 3f * Mathf.Sin(t * 0.45f);
            o[TailLift] = 4f + 4f * Mathf.Sin(t * 0.8f);
            o[Raise] = -45f + 3f * breath;
            o[Sweep] = 30f;
            o[Tilt] = -30f;
            if (paw < 1.2f)
            {
                float k = Mathf.Sin(paw / 1.2f * Mathf.PI);
                o[0] = (-18f + 10f * Mathf.Sin(paw * 9f)) * k;
                o[4] = 75f * k;
            }
        }

        // Galloping, u the way through a stride: each leg swings, its lower half folding as it comes forward.
        public static void Gallop(float[] o, float u)
        {
            for (int i = 0; i < 4; i++)
            {
                float ph = 2f * Mathf.PI * (u - Stride[i]), lift = Mathf.Pow(Mathf.Max(0f, -Mathf.Cos(ph)), 0.7f);
                o[i] = (i < 2 ? 28f : 26f) * Mathf.Sin(ph);
                o[4 + i] = i < 2 ? 75f * lift : -45f * lift;
            }
            o[Pitch] = 4f * Mathf.Sin(2f * Mathf.PI * (u - 0.3f));
            o[Bob] = 0.3f * Mathf.Sin(4f * Mathf.PI * u);
            o[NeckTurn] = 10f + 6f * Mathf.Sin(2f * Mathf.PI * (u - 0.45f));
            o[TailLift] = 40f + 6f * Mathf.Sin(2f * Mathf.PI * u);
            o[Raise] = -25f;
            o[Sweep] = 55f;
            o[Tilt] = -10f;
        }

        // In the air: forelegs tucked, hind legs trailing, the wings beating.
        public static void Leap(float[] o, float t)
        {
            System.Array.Clear(o, 0, N);
            o[0] = -50f; o[4] = 110f;
            o[1] = -45f; o[5] = 105f;
            o[2] = 40f; o[6] = 25f;
            o[3] = 35f; o[7] = 20f;
            o[Pitch] = -6f;
            o[NeckTurn] = -4f;
            o[TailLift] = 50f;
            o[Raise] = -20f + 35f * Mathf.Sin(t * 14f);
        }

        // Reared (the brain turns it up about its hind hooves): the forelegs paw the air, the wings go up.
        public static void Rear(float[] o, float t)
        {
            System.Array.Clear(o, 0, N);
            float s = Mathf.Sin(t * 9f);
            o[0] = -35f + 20f * s; o[4] = 80f;
            o[1] = -20f - 20f * s; o[5] = 100f;
            o[NeckTurn] = 22f;
            o[TailLift] = 10f;
            o[Raise] = -55f + 4f * s;
            o[Sweep] = 5f;
        }
    }
}
