using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HtfFortress
{
    // The fortress's land fish wear bodies of our own, one per kind, drawn the way the game draws its fish (Owen:
    // 「我們的魚要畫的更好一點像是這遊戲的畫風」; tools/make_foe_models.py). Every walker is the game's walking fish: a fish
    // body on a node its walk animation wiggles, between two sneakers it steps with. Ours goes on that node in the game
    // body's place, so it walks the game's walk on the game's sneakers; the game's body is hidden, its colliders, AI and
    // physics stay. The gear rides on ours.
    public static class FoeBodies
    {
        public static string ModelOf(EnemyKind k) => "Foe" + (k == EnemyKind.Mini ? EnemyKind.Splitter : k);
        private static readonly Color Gilt = new Color(0.98f, 0.8f, 0.32f); // an elite is gilded

        // Where a piece of gear sits along the body (nose +0.5 .. tail -0.5: hats on the head, the shield before the face,
        // the sack toward the tail, the staff ahead of the healer's tall fin), how far it's let down into the back, in body
        // heights (the shell wraps it), and its size on the body's width.
        private static (float along, float down, float size) Anchor(string gear) =>
            gear == "Helmet" || gear == "MinerHat" ? (0.27f, 0.04f, 1f) : gear == "ShieldBoard" ? (0.33f, 0f, 1f) : gear == "ThiefSack" ? (-0.12f, 0.02f, 1f)
            : gear == "Shell" ? (0f, 0.12f, 1f) : gear == "HealerStaff" ? (0.3f, 0.02f, 1.5f) : (0f, 0.03f, 1f);

        // False when this kind has no body here (or the walker has no body of the game's to put it in place of): it's dressed
        // the old way (Enemies.TryDress).
        public static bool Dress(Transform root, EnemyKind kind, bool elite, string gear)
        {
            string model = ModelOf(kind);
            if (!File.Exists(Path.Combine(Models.Dir ?? "", model + ".obj"))) return false;
            MeshFilter game = null;
            var hide = new List<Renderer>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh && mf.sharedMesh.name.StartsWith("DripFish")) { if (!game || mf.sharedMesh.name == "DripFish") game = mf; }
            if (!game) return false;
            // The game's fish came with its own sneakers (and eye/mouth pieces). Our body replaces all of it, otherwise
            // those old sneakers hang off the new fish at the old scale and make every kind look alike.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r) hide.Add(r);
            Transform node = game.transform;
            Vector3 fwd = Snap(node.InverseTransformDirection(root.forward)), up = Snap(node.InverseTransformDirection(root.up));
            var go = Models.Attach(model, node, Vector3.one * 0.5f);
            var ours = go.GetComponent<MeshFilter>().sharedMesh;
            var (scale, pos) = Fit(fwd, up, game.sharedMesh.bounds, ours.bounds, Enemies.ShapeOf(kind));
            go.transform.localRotation = Quaternion.LookRotation(fwd, up);
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            foreach (var r in hide) r.forceRenderingOff = true; // not `enabled`: the game turns that on and off itself
            var body = go.GetComponent<Renderer>();
            if (elite)
            {
                var cols = Models.ColorsOf(body);
                for (int i = 0; i < cols.Length; i++)
                    if (cols[i].maxColorComponent > 0.1f) Models.Recolor(body, i, Color.Lerp(cols[i], Gilt, 0.45f)); // the eyes stay black
            }
            Boots(go.transform, ours, kind);
            var b = ours.bounds;
            float size = Mathf.Max(b.size.x, 0.3f * b.size.z), hat = 0f;
            if (gear != null)
            {
                var (along, down, k) = Anchor(gear);
                Enemies.Wear(go.transform, gear, TopAt(ours, along) - Vector3.up * down * b.size.y, size * k);
                if (gear == "Helmet" || gear == "MinerHat") hat = (gear == "Helmet" ? 0.4f : 0.55f) * size; // the crown goes on top of it
            }
            if (elite) Enemies.Wear(go.transform, "Crown", TopAt(ours, 0.27f) + Vector3.up * hat, size * 0.7f);
            return true;
        }

        // Two small boots on the new body, in that kind's own colour: the old sneakers were the biggest reason every
        // fortress fish read as "another one of the same".
        private static void Boots(Transform body, Mesh mesh, EnemyKind kind)
        {
            var c = Enemies.DyeOf(kind);
            if (c.maxColorComponent < 0.05f || (c.r > 0.92f && c.g > 0.92f && c.b > 0.92f)) c = new Color(0.22f, 0.62f, 0.9f); // Runner's default is white
            var mat = Models.MakeShared(Color.Lerp(c, Color.black, 0.28f));
            var b = mesh.bounds;
            float y = b.min.y + 0.045f, z = -0.16f;
            foreach (float x in new[] { -0.085f, 0.085f })
            {
                var boot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(boot.GetComponent<Collider>());
                boot.name = "FoeBoot";
                boot.transform.SetParent(body, false);
                boot.transform.localPosition = new Vector3(x, y, z);
                boot.transform.localScale = new Vector3(0.075f, 0.07f, 0.17f);
                var r = boot.GetComponent<Renderer>();
                r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        // Our body in the game body's place, in that node's space: as long as the game's along its head axis, our own
        // proportions kept (the walker is stretched per kind, Enemies.ShapeOf: ours undoes it), centred where the game's was and
        // a touch higher (ours are rounder). fwd/up: the game body's head and up axes there; game/ours: the two meshes' bounds.
        public static (Vector3 scale, Vector3 pos) Fit(Vector3 fwd, Vector3 up, Bounds game, Bounds ours, Vector3 shape)
        {
            Vector3 right = Vector3.Cross(up, fwd); // where our +X goes (Quaternion.LookRotation(fwd, up))
            float len = Mathf.Abs(Vector3.Dot(game.size, fwd));
            float k = len / Mathf.Max(0.01f, ours.size.z);
            var scale = new Vector3(k * shape.z / shape.x, k * shape.z / shape.y, k);
            Vector3 c = Vector3.Scale(ours.center, scale);
            return (scale, game.center - (right * c.x + up * c.y + fwd * c.z) + up * (0.03f * len));
        }

        // The axis nearest to v (the game body's axes are the walker's, give or take the wiggle of its walk).
        public static Vector3 Snap(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            return ax >= ay && ax >= az ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : ay >= az ? new Vector3(0f, Mathf.Sign(v.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        // Our body's top at `along` its length from the middle (nose +0.5), in its own space: the hull's, not a fin's edge (the
        // fins are plates on the middle line). Each kind's vertices read once.
        private static readonly Dictionary<Mesh, Vector3[]> Verts = new Dictionary<Mesh, Vector3[]>();
        private static Vector3 TopAt(Mesh m, float along)
        {
            if (!Verts.TryGetValue(m, out var vs)) Verts[m] = vs = m.vertices;
            var b = m.bounds;
            float z = b.center.z + along * b.size.z, y = b.center.y;
            foreach (var v in vs)
                if (Mathf.Abs(v.z - z) < 0.06f * b.size.z && Mathf.Abs(v.x) > 0.02f && v.y > y) y = v.y;
            return new Vector3(0f, y, z);
        }
    }
}
