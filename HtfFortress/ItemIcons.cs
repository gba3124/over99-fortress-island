using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // Pictures of the game's own items for the vault (the game draws its hotbar from 3D meshes; it has no icons): an item's
    // meshes posed high above the world, taken once by a camera that sees only them, kept as a sprite. Asked for one that
    // isn't there yet, it's null for a frame or two (the caller shows the name meanwhile) and Ready fires when it is.
    public class ItemIcons : MonoBehaviour
    {
        public static ItemIcons Instance;
        public static event System.Action Ready;
        private static readonly Dictionary<byte, Sprite> Done = new Dictionary<byte, Sprite>();
        private static readonly List<byte> Wanted = new List<byte>();
        private const int Layer = 31, Size = 160;
        private static readonly Vector3 Stage = new Vector3(0f, 6000f, 0f);
        private bool _busy;

        private void Awake() => Instance = this;

        public static Sprite Of(byte id)
        {
            if (Done.TryGetValue(id, out var s)) return s;
            if (!Wanted.Contains(id)) Wanted.Add(id);
            return null;
        }

        private void Update()
        {
            if (_busy || Wanted.Count == 0) return;
            byte id = Wanted[0];
            Wanted.RemoveAt(0);
            StartCoroutine(Take(id));
        }

        // A key colour behind the item (the vault tile's own, so an edge blended into it can't be seen). Some of the game's
        // shaders write no alpha, so what differs from the background is the item, not what has alpha.
        private static readonly Color Key = new Color(0.16f, 0.18f, 0.22f, 1f);

        private IEnumerator Take(byte id)
        {
            _busy = true;
            var prefab = GameInfo.GetSpawnable(id);
            var root = prefab ? Pose(prefab) : null;
            if (!root) { BuildSystem.Log.LogInfo($"Item icon {id} ({(prefab ? prefab.name : "no prefab")}): nothing to draw"); Done[id] = null; _busy = false; yield break; }
            var b = Bounds(root);
            Texture2D tex = null;
            int drawn = 0;
            // On the default layer (the game's renderer leaves layer 31 out: nothing drew there), 6 km up where nothing else is;
            // our own layer only if that ever draws nothing.
            foreach (int layer in new[] { 0, Layer })
            {
                foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;
                var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
                var cam = new GameObject("ItemIconCamera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Key;
                cam.cullingMask = 1 << layer;
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.15f + 0.01f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 50f;
                cam.targetTexture = rt;
                Vector3 dir = new Vector3(-0.8f, 0.65f, -1f).normalized; // from the front, a little above and to the side
                cam.transform.position = b.center + dir * 10f;
                cam.transform.LookAt(b.center);
                yield return new WaitForEndOfFrame(); // the camera has drawn into rt by now
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                if (tex) Destroy(tex);
                tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "ItemIcon" + id, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Destroy(cam.gameObject);
                var px = tex.GetPixels32();
                var bg = px[0]; // a corner: always background (the item is framed with a margin)
                drawn = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > 6) { px[i].a = 255; drawn++; }
                    else px[i] = new Color32(0, 0, 0, 0);
                }
                tex.SetPixels32(px);
                tex.Apply();
                BuildSystem.Log.LogInfo($"Item icon {id} ({prefab.name}) on layer {layer}: {root.transform.childCount} meshes, size {b.size:F2}, {drawn} of {Size * Size} pixels drawn");
                if (drawn > 0) break;
            }
            Destroy(root);
            if (drawn == 0 && tex) Destroy(tex);
            Done[id] = drawn > 0 ? Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f) : null; // none: the vault writes its name
            _busy = false;
            Ready?.Invoke();
        }

        // The prefab's visible meshes (a skinned one in its bind pose), placed as in the prefab, on the icon layer.
        private static GameObject Pose(Item prefab)
        {
            var root = new GameObject("ItemIconStage");
            root.transform.position = Stage;
            var origin = prefab.transform.worldToLocalMatrix;
            int parts = 0;
            Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>() is MeshFilter mf ? mf.sharedMesh : null;
            // A gun is an animated rig with the hands on it: only the part the hotbar shows (Item.Mesh), or the gun is a speck.
            var all = prefab.GetComponentsInChildren<Renderer>(true);
            bool justTheItem = prefab is Weapon && prefab.Mesh && all.Any(r => MeshOf(r) == prefab.Mesh);
            foreach (var r in all)
            {
                if (justTheItem ? MeshOf(r) != prefab.Mesh : !r.enabled || !ActiveInPrefab(r.transform, prefab.transform) || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                Mesh mesh = MeshOf(r);
                if (!mesh) continue;
                var part = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
                part.layer = Layer;
                part.transform.SetParent(root.transform, false);
                var m = origin * r.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                var pr = part.GetComponent<MeshRenderer>();
                pr.sharedMaterials = r.sharedMaterials;
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                parts++;
            }
            if (parts > 0) return root;
            Destroy(root);
            return null;
        }

        private static bool ActiveInPrefab(Transform t, Transform root)
        {
            for (; t; t = t.parent) { if (!t.gameObject.activeSelf) return false; if (t == root) return true; }
            return true;
        }

        private static Bounds Bounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
