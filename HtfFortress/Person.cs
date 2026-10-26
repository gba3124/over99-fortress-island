using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // One of the village's people, as the game draws its own: the game's own body (a player's, from its prefabs: the tall
    // head with the big round eyes, the faceted body, the big hands), dressed in one of the game's own
    // outfits (SkinManager's presets: the lighthouse keeper's cap and beard, the shopkeeper's, the grillmaster's apron...),
    // and moved by turning its bones: walking, carrying, waving, the head turning to you, the eyes following you, the mouth
    // going while it talks. Owen: the ones modelled for the mod didn't look enough like the game's. `id`: Market, Smith,
    // Vault, Lighthouse (the keepers), HelperN (the stall's helpers). Without the game's body (it's always loaded once a
    // session is up) it falls back to those models (tools/make_people.py).
    public class Person : MonoBehaviour
    {
        // Where the eyes are over the feet (the keepers look from here).
        public static readonly Vector3 Eyes = new Vector3(0f, 1.58f, 0f);

        // Who wears what (SkinManager's fields). Skin: the body's own, as the game's own people have it (the game's colours are
        // a spot on its 32 x 32 palette texture as (u, v, 1); (0, 0, 0) leaves each part as it was made).
        private static readonly Dictionary<string, string> Keepers = new Dictionary<string, string>
        {
            ["Market"] = "_kioskLady", ["Smith"] = "_grillmaster", ["Vault"] = "_military", ["Lighthouse"] = "_lighthouseKeeper",
        };
        private static readonly string[] HelperClothes = { "_swampLady", "_tourist", "_scaredGuyInShorts", "_jacob", "_archer", "_andrei", "_gunStoreClerc", "_scientist" };

        private Transform _body, _head, _armUL, _armLL, _armUR, _armLR, _legUL, _legLL, _legUR, _legLR, _mouth, _handR, _cup;
        private Transform[] _eyes = new Transform[0];
        // The middle of the right hand on Hand_R (measured off the game's hand mesh), and CoffeeCup.obj's handle from its origin:
        // the mug is held by its handle there, upright, its body toward the person's middle. The game's people have hands about
        // 1.4 times ours: the mug is drawn as much bigger (8.5 cm across in a helper's hand).
        private static readonly Vector3 Palm = new Vector3(0.005f, 0.083f, 0.006f), CupGrip = new Vector3(0.055f, 0.047f, 0f);
        private const float CupScale = 1.3f;
        private readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();
        private Vector3 _bodyRest;
        // The old models (no game body): a model per part, each turning about its joint.
        private Transform _oBody, _oHead, _oArmL, _oArmR, _oLegL, _oLegR;

        public static Person Make(Transform parent, string id)
        {
            var root = new GameObject("Person" + id).transform;
            root.SetParent(parent, false);
            var p = root.gameObject.AddComponent<Person>();
            bool dressed = false;
            try { dressed = p.Dress(id); }
            catch (System.Exception e) { BuildSystem.Log.LogWarning("Couldn't dress " + id + " in the game's body: " + e); }
            if (!dressed)
            {
                foreach (Transform c in root.Cast<Transform>().ToList()) DestroyImmediate(c.gameObject);
                p._body = null;
                p.Modelled(id);
            }
            return p;
        }

        public GameObject[] Renderers => GetComponentsInChildren<Renderer>(true).Select(r => r.gameObject).ToArray();

        // ---------- the game's body ----------

        private static GameObject _template, _holder;
        private static GameObject Template()
        {
            if (_template) return _template;
            // A prefab's body (never in a scene, always loaded): a skeleton and the four skinned parts. The dead player's has the
            // hands on the forearms too (other players' hands are separate, steered by the game), so it's taken first.
            _template = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>()
                .Where(r => r && r.sharedMesh && r.sharedMesh.name == "CharacterHead" && !r.gameObject.scene.IsValid() && r.transform.parent && r.transform.parent.Find("Armature") && r.transform.parent.Find("Outfit"))
                .Select(r => r.transform.parent.gameObject)
                .OrderByDescending(c => c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(h => h.sharedMesh && h.sharedMesh.name == "LeftHand")).FirstOrDefault();
            return _template;
        }

        private bool Dress(string id)
        {
            var template = Template();
            var sm = AccessTools.Field(typeof(SkinManager), "_instance").GetValue(null) as SkinManager;
            if (!template || !sm) return false;
            if (!Keepers.TryGetValue(id, out var clothes))
            {
                int n = int.TryParse(id.Replace("Helper", ""), out int h) ? Mathf.Max(1, h) : 1;
                clothes = HelperClothes[(n - 1) % HelperClothes.Length];
            }
            var outfit = AccessTools.Field(typeof(SkinManager), clothes)?.GetValue(sm) as Clothes;
            if (!outfit) return false;
            // Copied under a switched-off holder, so nothing on it wakes up before it's stripped.
            if (!_holder) { _holder = new GameObject("FortressPersonHolder"); _holder.SetActive(false); DontDestroyOnLoad(_holder); }
            var body = Instantiate(template, _holder.transform, false);
            body.name = "Character";
            // Only the skeleton and the skin: nothing of the player's (scripts, colliders, name tag) comes along.
            foreach (var c in body.GetComponentsInChildren<Component>(true).Reverse())
                if (c && !(c is Transform) && !(c is SkinnedMeshRenderer)) DestroyImmediate(c); // (a missing script shows as null)
            foreach (var t in body.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("Canvas")).ToList()) if (t) DestroyImmediate(t.gameObject); // (one inside another goes with it)
            body.SetActive(false);
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = template.transform.lossyScale; // as big as another player looks
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = r.name == "Outfit" ? outfit.OutfitMesh : r.name == "Hat" ? outfit.HatMesh : r.name == "Accessory" ? outfit.AccessoryMesh : r.sharedMesh;
                if (!mesh) { r.gameObject.SetActive(false); continue; }
                r.sharedMesh = mesh;
                // The bones move it well out of its rest bounds: a box of 5 m round its root (in whichever of the two spaces
                // Unity takes it in) holds any pose, so one off screen is left alone (thirty helpers were skinned every frame).
                var root = r.rootBone ? r.rootBone : r.transform;
                float scale = Mathf.Max(1e-3f, Mathf.Min(Mathf.Abs(root.lossyScale.x), Mathf.Abs(r.transform.lossyScale.x)));
                r.localBounds = new Bounds(Vector3.zero, Vector3.one * (5f / scale));
                r.updateWhenOffscreen = false;
                ShaderManager.SetPlayerColors(r, Vector3.zero); // the body's own skin, as the game's own people have it
            }
            body.SetActive(true);
            var bones = body.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;
            _body = B("Body"); _head = B("Head"); _mouth = B("MouthLower");
            _armUL = B("ArmUpper_L"); _armLL = B("ArmLower_L"); _armUR = B("ArmUpper_R"); _armLR = B("ArmLower_R");
            _legUL = B("LegUpper_L"); _legLL = B("LegLower_L"); _legUR = B("LegUpper_R"); _legLR = B("LegLower_R");
            _handR = B("Hand_R");
            _eyes = new[] { B("Eye_L"), B("Eye_R") }.Where(e => e).ToArray();
            if (!_body || !_head || !_armUL || !_armUR || !_legUL || !_legUR) { Destroy(body); return false; }
            Stand(body.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh && r.sharedMesh.name == "CharacterHead"));
            return true;
        }

        // Standing: the skeleton as it was modelled (the bind pose), then the arms down by the sides, a little forward, and
        // the legs straight under the hips. What every move turns from.
        private void Stand(SkinnedMeshRenderer head)
        {
            if (head && head.sharedMesh && head.sharedMesh.bindposes.Length == head.bones.Length)
            {
                var binds = head.sharedMesh.bindposes;
                var order = head.bones.Select((b, i) => (b, i)).Where(x => x.b).OrderBy(x => Depth(x.b)).ToList();
                foreach (var (b, i) in order)
                {
                    var m = head.transform.localToWorldMatrix * binds[i].inverse;
                    b.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                }
            }
            Vector3 C(float x, float y, float z) => transform.TransformDirection(new Vector3(x, y, z).normalized);
            void Point(Transform b, Vector3 dir) { if (b) b.rotation = Quaternion.FromToRotation(b.up, dir) * b.rotation; }
            Point(_legUL, C(-0.04f, -1f, 0f)); Point(_legLL, C(0f, -1f, 0f));
            Point(_legUR, C(0.04f, -1f, 0f)); Point(_legLR, C(0f, -1f, 0f));
            Point(_armUL, C(-0.22f, -1f, 0.04f)); Point(_armLL, C(-0.06f, -1f, 0.22f));
            Point(_armUR, C(0.22f, -1f, 0.04f)); Point(_armLR, C(0.06f, -1f, 0.22f));
            foreach (var b in new[] { _body, _head, _mouth, _armUL, _armLL, _armUR, _armLR, _legUL, _legLL, _legUR, _legLR }.Concat(_eyes).Where(b => b))
                _rest[b] = b.localRotation;
            _bodyRest = _body.localPosition;
            // As tall as the game's people (eyes 1.58 m over the feet), whatever the copy's scale came out as; then the feet
            // on the ground (the root's height).
            float Low()
            {
                float low = float.MaxValue;
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.gameObject.activeSelf && r.sharedMesh))
                {
                    var baked = new Mesh();
                    r.BakeMesh(baked);
                    foreach (var v in baked.vertices) low = Mathf.Min(low, r.transform.TransformPoint(v).y);
                    Destroy(baked);
                }
                return low;
            }
            var character = _body.parent && _body.parent.parent ? _body.parent.parent : _body.parent;
            float feet = Low();
            if (feet < float.MaxValue && _eyes.Length > 0)
            {
                float eyes = _eyes.Average(e => e.position.y) - feet;
                if (eyes > 0.3f) character.localScale *= Eyes.y / eyes;
                feet = Low();
            }
            if (feet < float.MaxValue) _body.position += Vector3.up * (transform.position.y - feet);
            _bodyRest = _body.localPosition;
        }

        private static int Depth(Transform t) { int d = 0; while (t.parent) { t = t.parent; d++; } return d; }

        // A turn about one of the person's own axes (x right, y up, z ahead), as the bone stands at rest.
        private void Turn(Transform b, float degrees, Vector3 axis)
        {
            if (!b || !_rest.TryGetValue(b, out var rest)) return;
            var restWorld = (b.parent ? b.parent.rotation : Quaternion.identity) * rest;
            b.localRotation = rest * Quaternion.AngleAxis(degrees, Quaternion.Inverse(restWorld) * transform.TransformDirection(axis));
        }
        private void Reset(Transform b) { if (b && _rest.TryGetValue(b, out var rest)) b.localRotation = rest; }

        // A mug in the right hand at the helpers' coffee break (Helpers.Rest), in its enamel `color`; Pose holds it up in front.
        public void HoldCup(bool on, Color color)
        {
            if (!on) { if (_cup && _cup.gameObject.activeSelf) _cup.gameObject.SetActive(false); return; }
            if (!_cup)
            {
                var hand = _handR ? _handR : _oArmR ? _oArmR : transform;
                _cup = Models.Attach("CoffeeCup", hand, new Vector3(0.08f, 0.09f, 0.08f), new Vector3(0f, 0.045f, 0f), shadows: false).transform;
                if (_cup.GetComponent<Renderer>() is Renderer r) Models.Recolor(r, 0, color);
            }
            if (!_cup.gameObject.activeSelf) _cup.gameObject.SetActive(true);
        }

        // walk: the stride's phase (radians), stride 0..1 how hard it walks; carry: both hands out in front (a basket);
        // wave 0..1: the right hand up and waving (and the mouth going: they wave while they talk); lookYaw / lookPitch:
        // the head (degrees, pitch down positive); cup: the right hand holds the mug up in front, `lift` degrees higher (a
        // sip); talk 0..1: the mouth going.
        public void Pose(float walk, float stride, bool carry, float wave, float lookYaw, float lookPitch, bool cup = false, float lift = 0f, float talk = 0f)
        {
            if (!_body) { PoseModelled(walk, stride, carry, wave, lookYaw, lookPitch, cup, lift); return; }
            foreach (var b in _rest.Keys) Reset(b);
            float swing = Mathf.Sin(walk) * 30f * stride;
            // Legs: forward is a turn back about x; the knee bends on the leg that's lifting.
            Turn(_legUL, -swing, Vector3.right); Turn(_legUR, swing, Vector3.right);
            Turn(_legLL, Mathf.Max(0f, Mathf.Sin(walk + 1.2f)) * 40f * stride, Vector3.right);
            Turn(_legLR, Mathf.Max(0f, -Mathf.Sin(walk + 1.2f)) * 40f * stride, Vector3.right);
            if (carry)
            {
                Turn(_armUL, -72f, Vector3.right); Turn(_armUR, -72f, Vector3.right);
                Turn(_armLL, -18f, Vector3.right); Turn(_armLR, -18f, Vector3.right);
            }
            else
            {
                Turn(_armUL, swing * 0.8f, Vector3.right); Turn(_armUR, -swing * 0.8f, Vector3.right);
                Turn(_armLL, -8f - Mathf.Abs(swing) * 0.3f, Vector3.right); Turn(_armLR, -8f - Mathf.Abs(swing) * 0.3f, Vector3.right);
            }
            if (cup && !carry)
            {
                // The elbow by the side, the forearm up in front: the hand at the chest; a sip lifts both.
                Turn(_armUR, -10f - lift, Vector3.right);
                Turn(_armLR, -90f - lift * 0.6f, Vector3.right);
            }
            if (wave > 0f)
            {
                var up = Quaternion.AngleAxis((150f + Mathf.Sin(Time.time * 12f) * 15f) * wave, transform.forward);
                var restWorld = (_armUR.parent ? _armUR.parent.rotation : Quaternion.identity) * _rest[_armUR];
                _armUR.rotation = up * restWorld;
                Turn(_armLR, -25f * wave, Vector3.right);
                if (_mouth) Turn(_mouth, Mathf.Abs(Mathf.Sin(Time.time * 15f)) * 22f * wave, Vector3.right);
            }
            if (talk > 0f && _mouth && wave <= 0f) Turn(_mouth, Mathf.Abs(Mathf.Sin(Time.time * 13f)) * 18f * talk, Vector3.right);
            // The head turns, then the eyes stare on at whoever's close (as the game's own people do).
            var headRest = (_head.parent ? _head.parent.rotation : Quaternion.identity) * _rest[_head];
            _head.rotation = Quaternion.AngleAxis(lookYaw, transform.up) * Quaternion.AngleAxis(lookPitch, transform.right) * headRest;
            var cam = GameInfo.CurCamera;
            foreach (var e in _eyes)
            {
                Vector3 ahead = e.up;
                Vector3 to = cam ? cam.transform.position - e.position : ahead;
                if (cam && to.magnitude < 7f) e.rotation = Quaternion.FromToRotation(ahead, Vector3.RotateTowards(ahead, to.normalized, 40f * Mathf.Deg2Rad, 0f)) * e.rotation;
            }
            // A bounce at each step, and breathing.
            _body.localPosition = _bodyRest + _body.parent.InverseTransformVector(transform.up * (Mathf.Abs(Mathf.Sin(walk)) * 0.035f * stride + 0.006f * Mathf.Sin(Time.time * 2.1f)));
            if (cup && _handR) PlaceCup(_handR.TransformPoint(Palm));
        }

        // Upright whatever the hand does (it's coffee), its handle in the palm, sized to the person.
        private void PlaceCup(Vector3 grip)
        {
            if (!_cup || !_cup.gameObject.activeSelf) return;
            var rot = Quaternion.LookRotation(transform.forward, transform.up);
            float s = Mathf.Abs(transform.lossyScale.x) * CupScale;
            _cup.rotation = rot;
            _cup.localScale = Vector3.one * (s / Mathf.Max(1e-4f, Mathf.Abs(_cup.parent.lossyScale.x)));
            _cup.position = grip - rot * (CupGrip * s);
        }

        // ---------- the old models (tools/make_people.py), when the game's body isn't there ----------

        private void Modelled(string id)
        {
            string part = id.StartsWith("Helper") ? "Helper" + (((int.TryParse(id.Substring(6), out int n) ? n : 1) - 1) % 3 + 1) : id;
            Transform Part(string name, Vector3 at)
            {
                var joint = new GameObject(name).transform;
                joint.SetParent(transform, false);
                joint.localPosition = at;
                Models.Attach("Person" + part + name, joint, Vector3.one * 0.2f);
                return joint;
            }
            _oBody = Part("Body", Vector3.zero);
            _oHead = Part("Head", new Vector3(0f, 1.45f, 0f));
            _oArmL = Part("ArmL", new Vector3(-0.19f, 1.38f, 0f));
            _oArmR = Part("ArmR", new Vector3(0.19f, 1.38f, 0f));
            _oLegL = Part("LegL", new Vector3(-0.085f, 0.92f, 0f));
            _oLegR = Part("LegR", new Vector3(0.085f, 0.92f, 0f));
        }

        private void PoseModelled(float walk, float stride, bool carry, float wave, float lookYaw, float lookPitch, bool cup, float lift)
        {
            if (!_oBody) return;
            float swing = Mathf.Sin(walk) * 28f * stride;
            _oLegL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            _oLegR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            if (carry) _oArmL.localRotation = _oArmR.localRotation = Quaternion.Euler(-60f, 0f, 0f);
            else
            {
                _oArmL.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, -6f);
                _oArmR.localRotation = Quaternion.Euler(cup ? -55f - lift : swing * 0.8f, 0f, 6f);
            }
            if (wave > 0f) _oArmR.localRotation = Quaternion.Slerp(_oArmR.localRotation, Quaternion.Euler(0f, 0f, 150f + Mathf.Sin(Time.time * 12f) * 18f), wave);
            _oHead.localRotation = Quaternion.Euler(lookPitch, lookYaw, 0f);
            _oBody.localPosition = Vector3.up * Mathf.Abs(Mathf.Sin(walk)) * 0.035f * stride;
            if (cup) PlaceCup(_oArmR.TransformPoint(0f, -0.6f, 0f)); // the mitten at the end of the arm (tools/make_people.py)
        }
    }
}
