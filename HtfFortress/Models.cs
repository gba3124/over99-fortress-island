using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HtfFortress
{
    // Loads the Blender-made OBJ models next to the plugin dll (Models/*.obj + .mtl) and builds
    // materials from the game's own shader, since Unity's default material isn't in this build (renders magenta).
    public static class Models
    {
        public static string Dir;

        private class Model { public Mesh Mesh; public Color[] Colors; public Color[] Emits; }
        private static readonly Dictionary<string, Model> Cache = new Dictionary<string, Model>();
        private static Material _template;
        private static readonly Dictionary<Renderer, Color[]> BaseColors = new Dictionary<Renderer, Color[]>();
        private static readonly Dictionary<Renderer, Color[]> Originals = new Dictionary<Renderer, Color[]>();

        // ---------- materials ----------

        private static Material Template()
        {
            if (_template) return _template;
            var all = Resources.FindObjectsOfTypeAll<Material>();
            _template = all.FirstOrDefault(m => m && m.shader && m.shader.name == "Shader Graphs/DefaultShader")
                     ?? all.FirstOrDefault(m => m && m.shader && m.shader.name.Contains("Lit") && m.shader.isSupported);
            if (_template)
            {
                var sh = _template.shader;
                var props = Enumerable.Range(0, sh.GetPropertyCount()).Select(i => sh.GetPropertyName(i) + ":" + sh.GetPropertyType(i));
                BuildSystem.Log.LogInfo($"Material template '{_template.name}' shader '{sh.name}': {string.Join(", ", props)}");
            }
            else BuildSystem.Log.LogWarning("No game material found to copy; models may render magenta.");
            return _template;
        }

        // The game's DefaultShader takes albedo from a palette texture (_Colors), not a colour property,
        // so each material gets its own tiny solid texture that we repaint for tints.
        private static readonly Dictionary<Material, Texture2D> Swatch = new Dictionary<Material, Texture2D>();
        private static readonly Dictionary<Material, Color> Painted = new Dictionary<Material, Color>();

        public static Material Make(Color color, Color emit = default)
        {
            var t = Template();
            var m = t ? new Material(t) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Swatch[m] = tex;
            var sh = m.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                string n = sh.GetPropertyName(i);
                string ln = n.ToLowerInvariant();
                var type = sh.GetPropertyType(i);
                if (type == UnityEngine.Rendering.ShaderPropertyType.Texture && (ln == "_colors" || ln.Contains("main") || ln.Contains("base") || ln.Contains("albedo")))
                    m.SetTexture(n, tex);
            }
            if (emit.maxColorComponent > 0f)
            {
                if (m.HasProperty("_Emission")) m.SetFloat("_Emission", 1f);
                m.EnableKeyword("_EMISSION");
                color = Color.Lerp(color, emit, 0.5f);
            }
            SetMatColor(m, color);
            return m;
        }

        private static void SetMatColor(Material m, Color c)
        {
            if (Painted.TryGetValue(m, out var prev) && prev == c) return;
            Painted[m] = c;
            if (Swatch.TryGetValue(m, out var tex) && tex)
            {
                tex.SetPixels(new[] { c, c, c, c });
                tex.Apply(false);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        // ---------- spawning ----------

        // Builds the model as a child of `parent`. Falls back to a plain cube of `fallbackSize` if the OBJ is missing.
        public static GameObject Attach(string name, Transform parent, Vector3 fallbackSize, Vector3 fallbackOffset = default)
        {
            var model = Load(name);
            GameObject go;
            if (model != null)
            {
                go = new GameObject(name + "Model");
                go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = model.Colors.Select((c, i) => Make(c, model.Emits[i])).ToArray();
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.localScale = fallbackSize;
                go.GetComponent<Renderer>().sharedMaterial = Make(new Color(0.6f, 0.45f, 0.3f));
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = model != null ? Vector3.zero : fallbackOffset;
            go.layer = parent.gameObject.layer;
            var r = go.GetComponent<Renderer>();
            var cols = model != null ? model.Colors.ToArray() : new[] { new Color(0.6f, 0.45f, 0.3f) };
            BaseColors[r] = cols;
            Originals[r] = cols.ToArray();
            return go;
        }

        // t = 1 healthy, 0 nearly destroyed: fade the model's colours toward dark red.
        public static void Tint(GameObject root, float t)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!BaseColors.TryGetValue(r, out var baseCols)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < baseCols.Length; i++)
                    SetMatColor(mats[i], Color.Lerp(new Color(0.5f, 0.08f, 0.08f), baseCols[i], t));
            }
        }

        // Upgrades change the look: walls/floors go wood -> stone -> metal, turrets gain gold trim.
        public static void SetLevelLook(GameObject root, PieceType type, int level)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!Originals.TryGetValue(r, out var orig)) continue;
                BaseColors[r] = orig.Select(c => LevelColor(c, type, level)).ToArray();
            }
        }

        private static Color LevelColor(Color c, PieceType type, int level)
        {
            if (level <= 1) return c;
            float lum = c.grayscale;
            if (Catalog.IsTurret(type))
                return Color.Lerp(c, new Color(1f, 0.8f, 0.3f), level == 2 ? 0.2f : 0.45f);
            var target = level == 2 ? new Color(0.58f, 0.58f, 0.6f) : new Color(0.38f, 0.43f, 0.52f);
            return Color.Lerp(new Color(lum, lum, lum), target, 0.75f) * (0.7f + lum);
        }

        // Ghost preview: every material one flat colour.
        public static void Paint(GameObject root, Color c)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials) SetMatColor(m, c);
        }

        // ---------- OBJ ----------

        private static Model Load(string name)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            Model model = null;
            string obj = Path.Combine(Dir ?? "", name + ".obj");
            if (File.Exists(obj))
            {
                try { model = Parse(obj); }
                catch (System.Exception e) { BuildSystem.Log.LogWarning($"Failed to load {obj}: {e.Message}"); }
            }
            else BuildSystem.Log.LogWarning("Model not found: " + obj);
            Cache[name] = model;
            return model;
        }

        // Minimal OBJ: v / usemtl / f, flat shaded. X is flipped (Blender right-handed -> Unity left-handed) so winding is reversed.
        private static Model Parse(string path)
        {
            var inv = CultureInfo.InvariantCulture;
            var src = new List<Vector3>();
            var verts = new List<Vector3>();
            var subs = new List<List<int>>();
            var matNames = new List<string>();
            int cur = -1;
            foreach (var raw in File.ReadLines(path))
            {
                var p = raw.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "v")
                    src.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                else if (p[0] == "usemtl")
                {
                    cur = matNames.IndexOf(p[1]);
                    if (cur < 0) { matNames.Add(p[1]); subs.Add(new List<int>()); cur = subs.Count - 1; }
                }
                else if (p[0] == "f")
                {
                    if (cur < 0) { matNames.Add("default"); subs.Add(new List<int>()); cur = 0; }
                    var idx = p.Skip(1).Select(t => int.Parse(t.Split('/')[0], inv)).Select(i => i > 0 ? i - 1 : src.Count + i).ToArray();
                    for (int k = 1; k + 1 < idx.Length; k++)
                    {
                        int b = verts.Count;
                        verts.Add(src[idx[0]]); verts.Add(src[idx[k + 1]]); verts.Add(src[idx[k]]);
                        subs[cur].Add(b); subs[cur].Add(b + 1); subs[cur].Add(b + 2);
                    }
                }
            }

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var (kd, ke) = ParseMtl(Path.ChangeExtension(path, ".mtl"));
            return new Model
            {
                Mesh = mesh,
                Colors = matNames.Select(n => kd.TryGetValue(n, out var c) ? c : Color.gray).ToArray(),
                Emits = matNames.Select(n => ke.TryGetValue(n, out var c) ? c : Color.black).ToArray(),
            };
        }

        private static (Dictionary<string, Color>, Dictionary<string, Color>) ParseMtl(string path)
        {
            var inv = CultureInfo.InvariantCulture;
            var kd = new Dictionary<string, Color>();
            var ke = new Dictionary<string, Color>();
            if (!File.Exists(path)) return (kd, ke);
            string cur = null;
            foreach (var raw in File.ReadLines(path))
            {
                var p = raw.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "newmtl") cur = p[1];
                else if (cur != null && (p[0] == "Kd" || p[0] == "Ke") && p.Length >= 4)
                {
                    var c = new Color(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv));
                    (p[0] == "Kd" ? kd : ke)[cur] = c;
                }
            }
            return (kd, ke);
        }
    }
}
