using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HtfFortress
{
    // The ladder up the lighthouse's east side to its gallery (Owen: "燈塔的其中一面如果有梯子可以爬上去"). Walk into it and hold
    // W to climb (facing away from it, W takes you down), S the other way, nothing to hang there, Space to let go; at the top
    // you step onto the gallery, and walking off the gallery through the gap in its railing puts you on the ladder. Only
    // the local player's own movement is changed: where you are reaches everyone as always.
    [HarmonyPatch(typeof(PlayerMovement), "Move")]
    public static class Ladder
    {
        // In the lighthouse's own frame (tools/make_fortress_kit.py: Blender (x, y, z) is (-x, z, -y) here): the ladder's
        // rungs at x -1.62, 0.25 m either side of z 0; the gallery's floor (top) and edge.
        public const float FaceX = -1.62f, HalfWidth = 0.25f, DeckY = 4.36f, DeckR = 1.55f;
        public const float Off = 0.55f;  // how far out from the rungs you hang: clear of the gallery's edge
        private const float Speed = 2.4f; // m/s
        private static readonly AccessTools.FieldRef<PlayerMovement, Rigidbody> Rig = AccessTools.FieldRefAccess<PlayerMovement, Rigidbody>("_rig");
        private static readonly AccessTools.FieldRef<PlayerMovement, CapsuleCollider> Col = AccessTools.FieldRefAccess<PlayerMovement, CapsuleCollider>("_col");
        private static readonly AccessTools.FieldRef<PlayerMovement, Vector2> Input = AccessTools.FieldRefAccess<PlayerMovement, Vector2>("_moveInput");
        private static float _letGoUntil, _nextHint;
        public static bool Climbing { get; private set; }

        // Which way along the ladder the keys move you: +1 up, -1 down, 0 hang. W goes the way you face (toward the tower:
        // up), S the other way; from the ground only facing it (within 60 degrees) and pushing on starts a climb.
        public static int Way(float forward, float facing, bool grounded, float feet)
        {
            int w = forward > 0.1f ? 1 : forward < -0.1f ? -1 : 0;
            if (facing < -0.2f) w = -w;
            if (grounded && !(w > 0 && facing > 0.5f && feet < DeckY - 1f)) return 0;
            return w;
        }

        private static bool _broken;
        private static void Postfix(PlayerMovement __instance)
        {
            if (_broken) return;
            try { Climb(__instance); }
            catch (System.Exception e) { _broken = true; Climbing = false; BuildSystem.Log.LogError("Ladder off: " + e); } // never in the way of walking
        }

        private static void Climb(PlayerMovement __instance)
        {
            bool was = Climbing;
            Climbing = false;
            var me = Player.LocalPlayer;
            var core = Siege.Instance ? Siege.Instance.Lighthouse : null;
            if (!core || !me || me.Movement != __instance || Time.time < _letGoUntil) return;
            var rig = Rig(__instance);
            var col = Col(__instance);
            Vector3 p = core.InverseTransformPoint(rig.position);
            float feet = p.y - (col ? col.height * 0.5f : 0.9f), ahead = FaceX - p.x; // ahead: how far out in front of the rungs
            if (Mathf.Abs(p.z) > HalfWidth + 0.3f || ahead < -0.3f || ahead > Off + 0.35f || feet < -0.5f || feet > DeckY + 0.3f) return;
            Vector2 move = me.BlockInputs ? Vector2.zero : Input(__instance);
            Vector3 look = core.InverseTransformDirection(me.CurPlayerRot * Vector3.forward);
            float facing = new Vector2(look.x, look.z).normalized.x; // +1 looking at the tower
            bool grounded = __instance.Grounded;
            int way = Way(move.y, facing, grounded, feet);
            if (grounded && way <= 0) return; // standing at its foot, or on the gallery: walking as usual
            if (!me.BlockInputs && Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
            {
                _letGoUntil = Time.time + 0.6f;
                rig.linearVelocity = core.TransformDirection(new Vector3(-2.5f, 2.5f, 0f)); // a push off, away from the tower
                return;
            }
            Climbing = true;
            Vector3 v;
            if (way > 0 && feet > DeckY + 0.05f) v = new Vector3(2.2f, 0.4f, -p.z * 4f); // over the top: step in onto the gallery
            else
            {
                Vector2 pull = Vector2.ClampMagnitude(new Vector2(FaceX - Off - p.x, -p.z) * 6f, 2f); // onto the ladder's line
                v = new Vector3(pull.x, way * Speed, pull.y);
            }
            v = core.TransformDirection(v);
            if (rig.useGravity) v.y -= Physics.gravity.y * Time.fixedDeltaTime; // gravity takes this back off this step
            rig.linearVelocity = v;
            if (!was)
            {
                AutoTest.Count("ladder_climb");
                if (Time.time >= _nextHint) { _nextHint = Time.time + 90f; Hud.Banner(Hud.Keys(L10n.T("ladderHint")), 3.5f); }
            }
        }
    }
}
