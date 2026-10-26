using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // Procedural posing for the bosses: a head that watches whoever is nearest, claw and arm chains that lean toward
    // them, tentacles that follow — and all of it harder in the second form. Visual only: every client works it out from
    // what it can already see, nothing is networked, and nothing gameplay depends on it.
    //
    // The game's creatures are skinned meshes with named bones, so there is no IK rig to plug into: this walks the bone
    // list, keeps the chains whose names we recognise (tentacle/arm/claw/jaw…), and turns them toward the target a
    // little at a time. If a body has no bones we recognise (or it has a body of our own — the Eye, the Steed) the
    // component switches itself off and the boss looks exactly as it did before.
    public class BossIk : MonoBehaviour
    {
        public EnemyKind Kind;

        private sealed class Chain
        {
            public readonly List<Transform> Bones = new List<Transform>();
            public readonly List<Quaternion> Rest = new List<Quaternion>();
        }

        // Only chains that read as limbs. Legs are handled by BossBody's own step sway; tails and fins look wrong pulled
        // straight at a player.
        private static readonly string[] ReachPrefixes = { "claw", "arm", "hand", "tentacle", "tent", "jaw", "pincer", "whip" };

        private readonly List<Chain> _chains = new List<Chain>();
        private Transform _head;
        private Quaternion _headRest;
        private BossBody _body;
        private Vector3 _aim, _aimSmooth;
        private float _weight, _weightTarget;
        private float _nextAim;

        private void Start()
        {
            _body = GetComponent<BossBody>();
            // The Eye and the Steed wear bodies of our own (BossEye/BossSteed) and move their own parts.
            if (Kind == EnemyKind.Eye || Kind == EnemyKind.Steed) { enabled = false; return; }
            Collect();
            if (_chains.Count == 0 && !_head) { enabled = false; return; }
            _aimSmooth = transform.position + transform.forward * 6f;
            _aim = _aimSmooth;
        }

        private void Collect()
        {
            var bones = new List<Transform>();
            foreach (var sk in GetComponentsInChildren<SkinnedMeshRenderer>())
                foreach (var b in sk.bones)
                    if (b && !bones.Contains(b)) bones.Add(b);

            foreach (var group in bones.GroupBy(Prefix))
            {
                if (group.Key.Length < 3) continue;
                bool reach = ReachPrefixes.Any(p => group.Key.Contains(p));
                bool headish = group.Key.Contains("head") || group.Key.Contains("neck");
                if (!reach && !headish) continue;
                var ordered = group.OrderBy(Depth).ToList();
                if (reach && ordered.Count >= 3 && IsChain(ordered))
                {
                    var c = new Chain();
                    foreach (var b in ordered) { c.Bones.Add(b); c.Rest.Add(b.localRotation); }
                    _chains.Add(c);
                }
                else if (headish && !_head && ordered.Count > 0)
                {
                    _head = ordered[ordered.Count - 1];
                    _headRest = _head.localRotation;
                }
            }
            if (!_head)
            {
                var h = bones.FirstOrDefault(b => b && b.name.ToLowerInvariant().Contains("head"));
                if (h) { _head = h; _headRest = h.localRotation; }
            }
        }

        // "tentacle_03.001" -> "tentacle": the part of the name that says which kind of limb this is.
        private static string Prefix(Transform b)
        {
            string n = b.name.ToLowerInvariant();
            int i = n.Length;
            while (i > 0 && (char.IsDigit(n[i - 1]) || n[i - 1] == '_' || n[i - 1] == '.' || n[i - 1] == ' ')) i--;
            return n.Substring(0, i);
        }

        private static int Depth(Transform b)
        {
            int d = 0;
            for (var t = b; t; t = t.parent) d++;
            return d;
        }

        // Bones of one limb sit on top of one another: each one's parent is the one before it.
        private static bool IsChain(List<Transform> bones)
        {
            for (int i = 1; i < bones.Count; i++)
                if (!bones[i].IsChildOf(bones[i - 1])) return false;
            return true;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Time.time >= _nextAim)
            {
                _nextAim = Time.time + 0.1f; // ten times a second is plenty for a look-at, and cheap on ten bosses
                _aim = NearestPlayer(transform.position);
            }
            _aimSmooth = Vector3.Lerp(_aimSmooth, _aim, 1f - Mathf.Exp(-6f * dt));
            _weightTarget = _body && _body.Second ? 1f : 0.55f; // the second form reaches harder
            _weight = Mathf.MoveTowards(_weight, _weightTarget, dt * 1.5f);

            if (_head) LookAt(_head, _aimSmooth, 0.8f * _weight);
            foreach (var c in _chains) Reach(c, _aimSmooth, _weight);
        }

        private static Vector3 NearestPlayer(Vector3 from)
        {
            Vector3 best = from + Vector3.forward * 6f;
            float bestD = float.MaxValue;
            var players = PlayerManager.AlivePlayers;
            if (players == null) return best;
            foreach (var p in players)
            {
                if (!p || !p.Transform) continue;
                float d = (p.Transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p.Transform.position + Vector3.up * 1.2f; }
            }
            return best;
        }

        // Turn one bone toward a point, eased: never a snap, never a full spin (a head does not turn inside out).
        private static void LookAt(Transform bone, Vector3 target, float weight)
        {
            Vector3 dir = target - bone.position;
            if (dir.sqrMagnitude < 0.01f || weight <= 0.001f) return;
            var want = Quaternion.LookRotation(dir.normalized);
            bone.rotation = Quaternion.Slerp(bone.rotation, Quaternion.Slerp(bone.rotation, want, weight), 0.35f);
        }

        // Lean a chain at a point: the bone nearest the body turns most, the next one less, so it reads as reaching
        // rather than as the whole limb snapping to the target.
        private void Reach(Chain c, Vector3 target, float weight)
        {
            for (int i = 0; i < c.Bones.Count - 1; i++)
            {
                var b = c.Bones[i];
                if (!b) continue;
                Vector3 toChild = c.Bones[i + 1].position - b.position;
                Vector3 toTarget = target - b.position;
                if (toChild.sqrMagnitude < 1e-4f || toTarget.sqrMagnitude < 1e-4f) continue;
                var want = Quaternion.FromToRotation(toChild, toTarget) * b.rotation;
                float w = weight * 0.55f / (i + 1);
                b.rotation = Quaternion.Slerp(b.rotation, Quaternion.Slerp(b.rotation, want, w), 0.4f);
            }
        }
    }
}
