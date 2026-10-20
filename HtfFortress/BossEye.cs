using System.IO;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // The Eye at the End wears a body of our own (tools/make_boss_eye.py). It used to be the game's foot snail, painted
    // black, with two glowing balls hung in the air in front of it (Owen: 「眼睛根本沒黏在上面 做的很不精緻」). Now it's a giant
    // eye in a socket of black glass. Shut, its lids meet in a red crack; open, the eyeball stares at whoever is looking
    // (each screen at its own camera). A corona behind it and a ring of shards turn. The snail stays underneath, hidden,
    // for its network object, its health and its collider, which is fitted round ours. Every screen animates its own copy;
    // the brain's moves only tell it to open wide (eye.open), squint (broken), follow its beam, or rage (the second form).
    public class BossEye : MonoBehaviour
    {
        public static readonly string[] Parts = { "BossEyeShell", "BossEyeBall", "BossEyeLidTop", "BossEyeLidBottom", "BossEyeHalo", "BossEyeShards" };

        // The lids turn about the eye's x axis from shut (0) to open (top lid up = negative); 1.25 of that is wide, 0.18 a squint.
        public const float TopOpen = -58f, BottomOpen = 42f, Wide = 1.25f, Squint = 0.18f;
        public const float MaxLook = 32f;     // how far the eyeball turns from straight ahead
        public const float ShardTilt = -24f;  // the shards' ring, its front up: from the island it passes over the eye, not across it
        public const float WakeAfter = 12f;   // it opens by itself if no move has opened it by then (a skipped entrance)
        private const float PupilAhead = 2.4f; // the pupil, in front of the eyeball's middle (metres)

        private Transform _ball, _top, _bottom, _halo, _shards;
        private Light _light;
        private float _born, _open, _wideUntil, _squintUntil, _blinkAt, _blinkUntil, _lookUntil, _haloTurn, _shardTurn;
        private bool _awake, _rage;
        private Vector3 _lookAt;

        public static BossEye Of(int id) => BossFx.BodyOf(id) is Transform t && t ? t.GetComponent<BossEye>() : null;

        // Swaps the snail for our eye. False if the models aren't there: it stays the snail.
        public static bool Dress(Transform root)
        {
            if (root.GetComponent<BossEye>()) return true;
            if (Parts.Any(p => !File.Exists(Path.Combine(Models.Dir ?? "", p + ".obj")))) return false;
            var snail = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToList();
            var body = new GameObject("BossEyeBody").transform;
            body.SetParent(root, false);
            body.localScale = Vector3.one / root.localScale.x; // ours are in metres, the snail is blown up 95 times
            body.gameObject.layer = snail.Count > 0 ? snail[0].gameObject.layer : root.gameObject.layer;
            Transform Part(string name, Transform parent) => Models.Attach(name, parent, Vector3.one).transform;
            var eye = root.gameObject.AddComponent<BossEye>();
            Part("BossEyeShell", body);
            eye._ball = Part("BossEyeBall", body);
            eye._top = Part("BossEyeLidTop", body);
            eye._bottom = Part("BossEyeLidBottom", body);
            var gear = new GameObject("BossEyeGear", typeof(BossGear)).transform; // its corona and shards: not its body (bounds, paint)
            gear.SetParent(body, false);
            gear.gameObject.layer = body.gameObject.layer;
            eye._halo = Part("BossEyeHalo", gear);
            var tilt = new GameObject("BossEyeShardTilt").transform;
            tilt.SetParent(gear, false);
            tilt.localRotation = Quaternion.Euler(ShardTilt, 0f, 0f);
            eye._shards = Part("BossEyeShards", tilt);
            foreach (var r in snail) r.forceRenderingOff = true; // not `enabled`: the game turns that on and off itself
            // its glow in front of the eye, not high over it where the snail's size put it
            if (root.Find("BossGlow") is Transform glow && glow.GetComponent<Light>() is Light light)
            {
                glow.SetParent(body, false);
                glow.localPosition = Vector3.forward * (PupilAhead + 1.2f);
                light.range = 16f;
                eye._light = light;
            }
            BossLooks.FitCollider(root, body);
            return true;
        }

        // Where the eyeball points for something in `dir` (its socket's space): straight at it, or as far round as it turns.
        public static Vector3 Look(Vector3 dir, float maxDeg)
        {
            float d = dir.magnitude;
            if (d < 1e-5f) return Vector3.forward;
            dir /= d;
            float a = maxDeg * Mathf.Deg2Rad;
            if (dir.z >= Mathf.Cos(a)) return dir;
            var side = new Vector3(dir.x, dir.y, 0f);
            side = side.sqrMagnitude < 1e-8f ? Vector3.up : side.normalized; // straight behind: it rolls its eye up
            return Vector3.forward * Mathf.Cos(a) + side * Mathf.Sin(a);
        }

        // The pupil, where its beams come out.
        public Vector3 Pupil => _ball ? _ball.TransformPoint(Vector3.forward * PupilAhead) : transform.position;

        public void Flare(float seconds) { _awake = true; _wideUntil = Mathf.Max(_wideUntil, Time.time + seconds); }
        public void Reel(float seconds) => _squintUntil = Time.time + seconds;
        public void Rage(bool on) => _rage = on;
        public void LookAt(Vector3 at, float seconds) { _lookAt = at; _lookUntil = Time.time + seconds; }

        private void Start()
        {
            _born = Time.time;
            _blinkAt = _born + WakeAfter + Random.Range(2f, 5f);
        }

        private void LateUpdate()
        {
            float now = Time.time, dt = Time.deltaTime;
            if (dt <= 0f || !_ball) return;
            if (!_awake && now - _born > WakeAfter) _awake = true;
            bool wide = now < _wideUntil || _rage, reeling = now < _squintUntil;

            // The lids: shut till it first opens, then open (wide while it glares or rages, a squint while it reels), and a
            // blink now and then when it's calm. They shut fast and open slower.
            if (_awake && !wide && !reeling && now > _blinkAt) { _blinkUntil = now + 0.13f; _blinkAt = now + Random.Range(3.5f, 7.5f); }
            float open = !_awake || now < _blinkUntil ? 0f : reeling ? Squint : wide ? Wide : 1f;
            _open = Mathf.Lerp(_open, open, 1f - Mathf.Exp(-(open < _open ? 30f : 8f) * dt));
            float shake = reeling ? Mathf.Sin(now * 45f) * 1.5f : 0f;
            _top.localRotation = Quaternion.Euler(TopOpen * _open + shake, 0f, 0f);
            _bottom.localRotation = Quaternion.Euler(BottomOpen * _open - shake, 0f, 0f);

            // The eyeball: on its beam while it fires one, else on you.
            var cam = GameInfo.CurCamera;
            Vector3 target = now < _lookUntil ? _lookAt : cam ? cam.transform.position : _ball.position + transform.forward;
            var look = Look(_ball.parent.InverseTransformDirection(target - _ball.position), MaxLook);
            _ball.localRotation = Quaternion.Slerp(_ball.localRotation, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-(now < _lookUntil ? 14f : 5f) * dt));

            // The corona and the shards turn, faster in a rage; its glow swells while it glares.
            float pace = _rage ? 2.2f : 1f;
            _haloTurn += 7f * pace * dt;
            _shardTurn += 14f * pace * dt;
            _halo.localRotation = Quaternion.AngleAxis(_haloTurn, Vector3.forward);
            _shards.localRotation = Quaternion.AngleAxis(_shardTurn, Vector3.up);
            if (_light) _light.intensity = Mathf.Lerp(_light.intensity, !_awake ? 0.8f : wide ? 4f : 1.8f, 1f - Mathf.Exp(-4f * dt));
        }
    }
}
