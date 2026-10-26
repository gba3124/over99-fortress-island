using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HtfFortress
{
    // The weapons' effects, in the game's own effect language:
    //   matter (gas, foam, drops, dust): 8 x 8 pixel splats that dissolve, on copies of the game's own particle material
    //     (Shader Graphs/DefaultParticle: point-sampled sprite, noise dissolve), recoloured; bubbles and notes are pixel
    //     sprites drawn here the same size;
    //   energy (beam, sparks, rings): small solid shapes with strong emission that the game's bloom lights up, on copies of
    //     the game's EmissiveParticle (what its bullets and muzzle flashes use);
    //   the game's own named effects (ParticleManager: splashes, muzzle flashes, bullet hits, blood, smoke).
    // Purely visual and local to each machine.
    public class WeaponFx : MonoBehaviour
    {
        public static WeaponFx Instance;
        private void Awake() => Instance = this;

        // ---------- the game's own effects ----------

        public static void Game(string particle, Vector3 at, Vector3 dir = default)
        {
            if (ClientSettings.Reduced) // fewer effects: one of a kind every 0.1 s (a volley's hit splashes are one splash)
            {
                float now = Time.unscaledTime;
                if (GameLast.TryGetValue(particle, out float last) && now - last < 0.1f && now >= last) return;
                GameLast[particle] = now;
            }
            try { ParticleManager.Play(particle, at, dir); } catch { }
        }
        private static readonly Dictionary<string, float> GameLast = new Dictionary<string, float>();

        // ---------- pixel textures (point sampled, like the game's) ----------

        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        private static Texture2D Pixels(string name, string[] rows, Dictionary<char, Color> palette, bool repeat = false)
        {
            if (Textures.TryGetValue(name, out var t) && t) return t;
            int h = rows.Length, w = rows[0].Length;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Fortress" + name, filterMode = FilterMode.Point, wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp };
            var c = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    c[(h - 1 - y) * w + x] = palette.TryGetValue(rows[y][x], out var col) ? col : Color.clear;
            t.SetPixels(c);
            t.Apply(false, true);
            Textures[name] = t;
            return t;
        }

        private static readonly Color Rim = new Color(0.85f, 0.97f, 1f, 1f), Shine = Color.white, Inner = new Color(0.6f, 0.85f, 1f, 0.18f), Tint = new Color(0.75f, 0.92f, 1f, 0.55f);

        // A soap bubble: a bright rim, a glint, clear inside.
        public static Texture2D BubbleTex => Pixels("Bubble", new[]
        {
            "................",
            ".....oooooo.....",
            "...oo::::::oo...",
            "..o::ww::::::o..",
            "..o:www::::::o..",
            ".o::ww::::::::o.",
            ".o::::::::::::o.",
            ".o::::::::::::o.",
            ".o::::::::::::o.",
            ".o:::::::::::to.",
            ".o::::::::::tto.",
            "..o::::::::tto..",
            "..o::::::::to...",
            "...oo::::::oo...",
            ".....oooooo.....",
            "................",
        }, new Dictionary<char, Color> { { 'o', Rim }, { ':', Inner }, { 'w', Shine }, { 't', Tint } });

        // A music note.
        public static Texture2D NoteTex => Pixels("Note", new[]
        {
            "..........",
            ".....##...",
            ".....###..",
            ".....#.##.",
            ".....#..#.",
            ".....#....",
            ".....#....",
            "..####....",
            ".#####....",
            ".####.....",
            "..##......",
        }, new Dictionary<char, Color> { { '#', Color.white } });

        // A twisted rope, along u.
        public static Texture2D RopeTex => Pixels("Rope", new[] { "aabbaabb", "abbaabba", "bbaabbaa", "baabbaab" },
            new Dictionary<char, Color> { { 'a', new Color(0.78f, 0.62f, 0.4f) }, { 'b', new Color(0.55f, 0.4f, 0.24f) } }, true);

        // From above: three arms of foam winding into the middle over three blues of water (the whirlpool's funnel).
        public static Texture2D SpiralTex
        {
            get
            {
                if (Textures.TryGetValue("Spiral", out var t) && t) return t;
                const int n = 48;
                t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "FortressSpiral", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var c = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n - 0.5f, r = Mathf.Sqrt(u * u + v * v) * 2f, a = Mathf.Atan2(v, u);
                        if (r > 1f) { c[y * n + x] = Color.clear; continue; }
                        float arm = Mathf.Sin(a * 3f + r * 10f);
                        Color col = arm > 0.75f ? new Color(0.92f, 0.98f, 1f, 0.95f) : arm > 0.2f ? new Color(0.45f, 0.75f, 1f, 0.75f)
                                  : arm > -0.4f ? new Color(0.22f, 0.5f, 0.9f, 0.65f) : new Color(0.1f, 0.3f, 0.7f, 0.6f);
                        if (r > 0.9f) col.a *= 0.5f;
                        c[y * n + x] = col;
                    }
                t.SetPixels(c);
                t.Apply(false, true);
                Textures["Spiral"] = t;
                return t;
            }
        }

        // ---------- materials (copies of the game's own) ----------

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static Material GameMat(string name) => Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m && m.name == name);

        private static Material Cached(string key, System.Func<Material> make)
        {
            if (Mats.TryGetValue(key, out var m) && m) return m;
            m = make();
            if (m) m.name = "Fortress" + key;
            Mats[key] = m;
            return m;
        }

        // A pixel splat in colour `c` (from: DustParticles = soft dust, BloodParticle / SplashParticle = solid splat);
        // `tex` replaces the splat. Falls back to the built-in sprite shader.
        public static Material Splat(string key, Color c, string from = "DustParticles", Texture tex = null) => Cached("Splat" + key, () =>
        {
            var src = GameMat(from) ?? GameMat("DustParticles");
            Material m;
            if (src)
            {
                m = new Material(src);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                if (tex && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            else
            {
                m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = c };
                m.renderQueue = 3050;
            }
            return m;
        });

        // A solid shape glowing `glow` times its colour (the game's bloom halos it), like the game's bullets and flashes.
        public static Material Emissive(string key, Color c, float glow) => Cached("Emit" + key, () =>
        {
            var src = GameMat("EmissiveParticle");
            var m = src ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", Texture2D.whiteTexture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * glow);
            m.EnableKeyword("_EMISSION");
            return m;
        });

        // The game's see-through water (lit, glossy), recoloured, optionally with a texture (the funnel's spiral).
        public static Material Water(string key, Color c, Texture tex = null) => Cached("Water" + key, () =>
        {
            var src = GameMat("WaterParticle");
            if (!src) return Splat(key, c, "SplashParticle", tex);
            var m = new Material(src);
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", tex ? tex : Texture2D.whiteTexture);
            m.SetFloat("_Cutoff", 0.05f);
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_Cull", 0f); // both sides (the funnel is seen from above and below)
            return m;
        });

        // A camera-facing pixel sprite (bubbles, notes): the built-in sprite shader, point sampled.
        public static Material Sprite(string key, Texture tex, Color c) => Cached("Sprite" + key, () =>
        {
            var m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = c };
            m.renderQueue = 3060;
            return m;
        });

        public static Material RopeMat => Cached("Rope", () => new Material(Shader.Find("Sprites/Default")) { mainTexture = RopeTex });

        // ---------- meshes ----------

        private static Mesh _cube, _torus, _cyl, _quad, _funnel;
        public static Mesh Cube => _cube ? _cube : _cube = PrimitiveMesh(PrimitiveType.Cube);
        public static Mesh Quad => _quad ? _quad : _quad = PrimitiveMesh(PrimitiveType.Quad);
        public static Mesh Cyl => _cyl ? _cyl : _cyl = BuildCyl(6);
        public static Mesh Torus => _torus ? _torus : _torus = BuildTorus(0.5f, 0.028f, 32, 4);
        private static Mesh _ball;
        public static Mesh Ball => _ball ? _ball : _ball = PrimitiveMesh(PrimitiveType.Sphere);

        // Bolts of energy running along a beam: glowing chips, no gravity, no drag.
        public static ParticleSystem Bolts(string colour) => System("Bolts" + colour, () => new Spec
        {
            Mat = Emissive("Bolt" + colour, SparkColour(colour), 9f), Mesh = Cube, Life = new Vector2(0.2f, 0.2f), Speed = Vector2.zero,
            Size = new Vector2(0.06f, 0.1f), Shape = ParticleSystemShapeType.Sphere, Radius = 0.01f, Max = 400,
        });

        // A pixel sprite facing the camera (a bubble, a big bubble round a fish).
        public static Transform Billboard(Texture tex, string key, Color c)
        {
            var t = Piece(Quad, Sprite(key, tex, c));
            return t;
        }

        public static void Face(Transform t)
        {
            var cam = GameInfo.CurCamera;
            if (t && cam) t.rotation = cam.transform.rotation;
        }

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }

        private static Mesh Flat(List<Vector3> tris)
        {
            var m = new Mesh();
            m.SetVertices(tris);
            m.SetTriangles(Enumerable.Range(0, tris.Count).ToArray(), 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // Along +Y from 0 to 1, 1 across, flat sided (a hexagonal beam reads as the game's chunky shapes).
        private static Mesh BuildCyl(int sides)
        {
            var tris = new List<Vector3>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.5f, p1 = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * 0.5f;
                tris.AddRange(new[] { p0, p0 + Vector3.up, p1 + Vector3.up, p0, p1 + Vector3.up, p1, Vector3.up, p1 + Vector3.up, p0 + Vector3.up, Vector3.zero, p0, p1 });
            }
            return Flat(tris);
        }

        // In the x/y plane (facing +z), 1 across: rings that fly along their axis.
        private static Mesh BuildTorus(float R, float r, int segs, int sides)
        {
            Vector3 P(int i, int j)
            {
                float a = i * Mathf.PI * 2f / segs, b = j * Mathf.PI * 2f / sides;
                Vector3 c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                return c * (R + r * Mathf.Cos(b)) + Vector3.forward * r * Mathf.Sin(b);
            }
            var tris = new List<Vector3>();
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < sides; j++)
                    tris.AddRange(new[] { P(i, j), P(i + 1, j + 1), P(i, j + 1), P(i, j), P(i + 1, j), P(i + 1, j + 1) });
            return Flat(tris);
        }

        // A whirlpool's funnel: from a narrow throat up to the rim, mapped from above.
        public static Mesh Funnel
        {
            get
            {
                if (_funnel) return _funnel;
                const int seg = 40, rings = 8;
                var v = new List<Vector3>();
                var uv = new List<Vector2>();
                var t = new List<int>();
                for (int j = 0; j <= rings; j++)
                {
                    float k = j / (float)rings;
                    float r = Mathf.Lerp(0.08f, 1f, k);
                    float y = -0.6f * (1f - k) * (1f - k);
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i * Mathf.PI * 2f / seg;
                        var p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                        v.Add(p);
                        uv.Add(new Vector2(0.5f + p.x * 0.5f, 0.5f + p.z * 0.5f));
                    }
                }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = j * (seg + 1) + i, b = a + seg + 1;
                        t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
                _funnel = new Mesh();
                _funnel.SetVertices(v);
                _funnel.SetUVs(0, uv);
                _funnel.SetTriangles(t, 0);
                _funnel.RecalculateNormals();
                _funnel.RecalculateBounds();
                return _funnel;
            }
        }

        public static Transform Piece(Mesh mesh, Material mat, Transform parent = null)
        {
            var go = new GameObject("FortressFx");
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // ---------- particle systems: one shared world-space system per kind, emitted where needed ----------

        public class Spec
        {
            public Material Mat;
            public Mesh Mesh; // set: solid shapes (render mode Mesh); otherwise camera-facing sprites
            public Vector2 Life = new Vector2(0.3f, 0.6f), Speed = new Vector2(1f, 3f), Size = new Vector2(0.1f, 0.2f), Spin = Vector2.zero;
            public Gradient Color;
            public AnimationCurve SizeOverLife;
            public float Gravity, Drag, Noise;
            public int Max = 400;
            public ParticleSystemShapeType Shape = ParticleSystemShapeType.Sphere;
            public float Radius = 0.05f, Angle = 25f;
            public bool FaceSystem; // turned like the system (rings flying along it), not toward the camera
        }

        public static Gradient Fade(Color a, Color b, float hold = 0.6f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, hold), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        public static AnimationCurve Curve(params float[] kv)
        {
            var c = new AnimationCurve();
            for (int i = 0; i + 1 < kv.Length; i += 2) c.AddKey(kv[i], kv[i + 1]);
            return c;
        }

        private static readonly Dictionary<string, ParticleSystem> Systems = new Dictionary<string, ParticleSystem>();

        public static ParticleSystem System(string name, System.Func<Spec> spec)
        {
            if (Systems.TryGetValue(name, out var ps) && ps) return ps;
            var s = spec();
            var go = new GameObject("FortressFx" + name);
            DontDestroyOnLoad(go);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = s.Max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.Life.x, s.Life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.Speed.x, s.Speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(s.Size.x, s.Size.y);
            if (s.FaceSystem) main.startRotation = 0f;
            else if (s.Mesh)
            {
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            }
            else main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = s.Gravity;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = s.Shape;
            shape.radius = s.Radius;
            shape.angle = s.Angle;
            if (s.Color != null) { var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(s.Color); }
            if (s.SizeOverLife != null) { var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, s.SizeOverLife); }
            if (s.Drag > 0f) { var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = s.Drag; lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = true; }
            if (s.Spin != Vector2.zero) { var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(s.Spin.x, s.Spin.y); }
            if (s.Noise > 0f) { var n = ps.noise; n.enabled = true; n.strength = s.Noise; n.frequency = 0.6f; n.scrollSpeed = 0.4f; n.quality = ParticleSystemNoiseQuality.Low; }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s.Mat;
            r.renderMode = s.Mesh ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
            if (s.Mesh) r.mesh = s.Mesh;
            r.alignment = s.FaceSystem ? ParticleSystemRenderSpace.Local : ParticleSystemRenderSpace.View;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            Systems[name] = ps;
            return ps;
        }

        // Emit n from `at`, the system's shape turned toward `dir`; optional colour / size / velocity / lifetime.
        public static void Emit(ParticleSystem ps, Vector3 at, int n, Vector3 dir = default, Color? color = null, float size = -1f, Vector3? velocity = null, float life = -1f)
        {
            if (!ps || (n = Thin(n, Random.value, ClientSettings.Reduced)) <= 0) return;
            ps.transform.SetPositionAndRotation(at, dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : Quaternion.identity);
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            if (color.HasValue) ep.startColor = color.Value;
            if (size > 0f) ep.startSize = size;
            if (velocity.HasValue) ep.velocity = velocity.Value;
            if (life > 0f) ep.startLifetime = life;
            ps.Emit(ep, n);
        }

        // Fewer effects (ClientSettings.Reduced): two in five of every emission, a single one kept at that chance (`roll`).
        public const float ReducedShare = 0.4f;
        public static int Thin(int n, float roll, bool reduced)
        {
            if (!reduced || n <= 0) return n;
            float keep = n * ReducedShare;
            return (int)keep + (roll < keep - (int)keep ? 1 : 0);
        }

        // ---------- the kinds ----------

        // Glowing chips (the game's bullets and muzzle flashes are such glowing chunks): one system per colour.
        // Found by colour without building its name (and a closure) on every hit: see BossVfx.Sparks.
        private static readonly Dictionary<string, ParticleSystem> SparksOf = new Dictionary<string, ParticleSystem>();
        public static ParticleSystem Sparks(string colour) => SparksOf.TryGetValue(colour, out var ps) && ps ? ps : SparksOf[colour] = MakeSparks(colour);
        private static ParticleSystem MakeSparks(string colour) => System("Sparks" + colour, () => new Spec
        {
            Mat = Emissive("Spark" + colour, SparkColour(colour), 7f), Mesh = Cube,
            Life = new Vector2(0.2f, 0.45f), Speed = new Vector2(3f, 8f), Size = new Vector2(0.05f, 0.1f), Gravity = 1.2f, Drag = 1.5f,
            SizeOverLife = Curve(0f, 1f, 0.6f, 0.8f, 1f, 0f), Shape = ParticleSystemShapeType.Cone, Angle = 50f, Radius = 0.02f, Max = 600,
        });

        public static Color SparkColour(string c) => c switch
        {
            "Cyan" => new Color(0.4f, 0.95f, 1f),
            "Gold" => new Color(1f, 0.78f, 0.3f),
            "Green" => new Color(0.55f, 1f, 0.3f),
            "Blue" => new Color(0.4f, 0.7f, 1f),
            "Pink" => new Color(1f, 0.5f, 0.95f),
            "Orange" => new Color(1f, 0.55f, 0.2f),
            "Red" => new Color(1f, 0.28f, 0.22f),
            "Violet" => new Color(0.65f, 0.45f, 1f),
            _ => new Color(1f, 0.95f, 0.8f),
        };

        // Rings of sound / shock: glowing hoops that grow as they fly.
        public static ParticleSystem Rings => System("Rings", () => new Spec
        {
            Mat = Emissive("Ring", new Color(1f, 0.62f, 0.25f), 1.1f), Mesh = Torus, FaceSystem = true,
            Life = new Vector2(0.5f, 0.5f), Speed = Vector2.zero, Size = new Vector2(1f, 1f), SizeOverLife = Curve(0f, 0.12f, 0.8f, 0.9f, 1f, 1f), Max = 40,
        });

        public static ParticleSystem Gas => System("Gas", () => new Spec
        {
            Mat = Splat("Gas", new Color(0.55f, 0.95f, 0.25f, 1f)), Life = new Vector2(1.4f, 2.4f), Speed = new Vector2(0.1f, 0.4f), Size = new Vector2(1f, 2f),
            Gravity = -0.03f, Color = Fade(Color.white, new Color(0.7f, 0.85f, 0.55f), 0.55f), SizeOverLife = Curve(0f, 0.5f, 0.4f, 1f, 1f, 1.2f),
            Spin = new Vector2(-0.6f, 0.6f), Noise = 0.25f, Max = 500,
        });

        public static ParticleSystem Flame => FlameFor(new Color(1f, 0.52f, 0.12f));
        // One fire system per branch palette (orange, red, white-hot, blue flame): the colour is the material, not a tint
        // that would turn an orange texture green.
        public static ParticleSystem FlameFor(Color c)
        {
            string key = "Flame" + ColorUtility.ToHtmlStringRGB(c);
            return System(key, () => new Spec
            {
                Mat = Splat(key, c), Life = new Vector2(0.22f, 0.48f), Speed = new Vector2(3f, 7f), Size = new Vector2(0.5f, 1.1f),
                Gravity = -0.04f, Color = Fade(Color.Lerp(Color.white, c, 0.25f), new Color(c.r * 0.15f, c.g * 0.15f, c.b * 0.15f, 0f), 0.65f),
                SizeOverLife = Curve(0f, 0.55f, 0.35f, 0.9f, 1f, 1.35f), Drag = 1.4f, Noise = 0.22f, Max = 600,
                Shape = ParticleSystemShapeType.Cone, Angle = 32f, Radius = 0.12f,
            });
        }

        public static ParticleSystem Dust => System("Dust", () => new Spec
        {
            Mat = Splat("Dust", new Color(0.62f, 0.58f, 0.5f, 1f)), Life = new Vector2(0.5f, 0.9f), Speed = new Vector2(0.5f, 2f), Size = new Vector2(0.4f, 0.8f),
            Gravity = 0.15f, Color = Fade(Color.white, Color.white, 0.4f), SizeOverLife = Curve(0f, 0.6f, 1f, 1.3f), Drag = 2f, Max = 300,
        });

        public static ParticleSystem Foam => System("Foam", () => new Spec
        {
            Mat = Splat("Foam", new Color(0.95f, 0.98f, 1f, 1f), "SplashParticle"), Life = new Vector2(0.5f, 1f), Speed = new Vector2(0.3f, 1.2f), Size = new Vector2(0.35f, 0.7f),
            Color = Fade(Color.white, Color.white, 0.5f), SizeOverLife = Curve(0f, 0.7f, 1f, 1.2f), Spin = new Vector2(-1f, 1f), Max = 700,
        });

        public static ParticleSystem Drops => System("Drops", () => new Spec
        {
            Mat = Splat("Drop", new Color(0.4f, 0.68f, 1f, 1f), "SplashParticle"), Life = new Vector2(0.4f, 0.8f), Speed = new Vector2(2f, 5f), Size = new Vector2(0.12f, 0.24f),
            Gravity = 1.4f, Color = Fade(Color.white, Color.white, 0.6f), Shape = ParticleSystemShapeType.Cone, Angle = 35f, Max = 700,
        });

        public static ParticleSystem Bubbles => System("Bubbles", () => new Spec
        {
            Mat = Sprite("Bubble", BubbleTex, Color.white), Life = new Vector2(0.6f, 1.2f), Speed = new Vector2(0.3f, 1f), Size = new Vector2(0.12f, 0.25f),
            Gravity = -0.25f, Color = Fade(Color.white, Color.white, 0.75f), Noise = 0.5f, Max = 500,
        });

        public static ParticleSystem Notes => System("Notes", () => new Spec
        {
            Mat = Sprite("Note", NoteTex, new Color(1f, 0.9f, 0.55f, 1f)), Life = new Vector2(0.8f, 1.2f), Speed = new Vector2(3f, 6f), Size = new Vector2(0.6f, 0.9f),
            Color = Fade(Color.white, new Color(1f, 0.8f, 0.4f), 0.7f), Spin = new Vector2(-1.5f, 1.5f), Noise = 1f, Drag = 1.2f,
            Shape = ParticleSystemShapeType.Cone, Angle = 28f, Max = 200,
        });

        // ---------- lines ----------

        public static LineRenderer Line(string name, Material mat, float width)
        {
            var go = new GameObject("FortressFx" + name);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 2;
            lr.widthMultiplier = width;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        // A rope hanging between two ends (verlet points: slack sags and swings, a pull snaps it straight).
        public class Rope
        {
            private readonly LineRenderer _line;
            private readonly Vector3[] _p, _old;

            public Rope(int points, float width)
            {
                _line = Line("Rope", RopeMat, width);
                _line.positionCount = points;
                _p = new Vector3[points];
                _old = new Vector3[points];
            }

            // Laid out straight from a to b (once, when the claw leaves the gun).
            public void Start(Vector3 a, Vector3 b)
            {
                for (int i = 0; i < _p.Length; i++) _p[i] = _old[i] = Vector3.Lerp(a, b, i / (float)(_p.Length - 1));
            }

            // `slack`: how much longer than the gap the rope is (0: taut).
            public void Step(Vector3 a, Vector3 b, float slack)
            {
                int n = _p.Length;
                float dt = Mathf.Min(Time.deltaTime, 0.033f);
                for (int i = 1; i < n - 1; i++)
                {
                    Vector3 v = (_p[i] - _old[i]) * 0.9f;
                    _old[i] = _p[i];
                    _p[i] += v + Vector3.down * 9.8f * dt * dt;
                }
                _p[0] = a; _p[n - 1] = b;
                float seg = (b - a).magnitude * (1f + slack) / (n - 1);
                for (int k = 0; k < 12; k++)
                    for (int i = 0; i < n - 1; i++)
                    {
                        Vector3 d = _p[i + 1] - _p[i];
                        float len = d.magnitude;
                        if (len < 1e-5f || len <= seg) continue; // a rope only pulls; it never pushes
                        Vector3 fix = d * ((len - seg) / len);
                        bool aFixed = i == 0, bFixed = i + 1 == n - 1;
                        if (aFixed && !bFixed) _p[i + 1] -= fix;
                        else if (bFixed && !aFixed) _p[i] += fix;
                        else if (!aFixed) { _p[i] += fix * 0.5f; _p[i + 1] -= fix * 0.5f; }
                    }
                _line.SetPositions(_p);
            }

            public void Destroy() { if (_line) Object.Destroy(_line.gameObject); }
        }

        // Foam going round and round a point (a whirlpool): its own system, so the particles orbit its middle.
        public static ParticleSystem Orbit(Vector3 at, float radius)
        {
            var go = new GameObject("FortressFxOrbit");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.42f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.rateOverTime = 45f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.55f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // lying flat
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = vel.y = vel.z = 0f;
            vel.orbitalX = 0f; vel.orbitalZ = 0f;
            vel.orbitalY = 2.6f;  // round the middle, the funnel's way
            vel.radial = -0.9f;   // and drawn in
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(Color.white, Color.white, 0.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Splat("Foam", new Color(0.95f, 0.98f, 1f, 1f), "SplashParticle");
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        public void Clear()
        {
            foreach (var ps in Systems.Values) if (ps) ps.Clear();
        }
    }
}
