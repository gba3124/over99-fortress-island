using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // The game's own plants on the fortress island (grass, beach grass and plants, ferns, small pines on the rocks), drawn
    // with the meshes and wind-swayed materials of its islands, where tools/make_island.py put them
    // (Models/FortressIsland.decor.txt, island-local). The same file marks the tide pools, which slow fish wading in.
    public class IslandDecor : MonoBehaviour
    {
        public readonly List<(Vector3 at, float radius)> Pools = new List<(Vector3, float)>();
        private readonly List<(string kind, Matrix4x4 m)> _wanted = new List<(string, Matrix4x4)>();
        private readonly List<(Mesh mesh, Material mat, List<Matrix4x4> batch)> _batches = new List<(Mesh, Material, List<Matrix4x4>)>();
        private bool _built;
        private Vector3 _builtAt;

        public void Load(string file)
        {
            if (!File.Exists(file)) { BuildSystem.Log.LogWarning("No island decor file: " + file); return; }
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in File.ReadAllLines(file))
            {
                var p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 6 || p[0].StartsWith("#")) continue;
                var at = new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)); // already the game's frame
                float yaw = float.Parse(p[4], inv), scale = float.Parse(p[5], inv);
                if (p[0] == "Pool") Pools.Add((at, scale));
                else _wanted.Add((p[0], Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale)));
            }
        }

        public bool InPool(Vector3 world)
        {
            Vector3 local = world - transform.position;
            foreach (var (at, r) in Pools)
                if (local.y < at.y + 1.2f && new Vector2(local.x - at.x, local.z - at.z).sqrMagnitude < r * r) return true;
            return false;
        }

        // The game fills its instance table on load; build the batches once it's there.
        private static Dictionary<string, InstanceManager.InstanceType> Types() =>
            AccessTools.Field(typeof(InstanceManager), "_instanceTypeDic")?.GetValue(null) as Dictionary<string, InstanceManager.InstanceType>;

        private void Build()
        {
            var types = Types();
            if (types == null || types.Count == 0) return;
            _built = true;
            _builtAt = transform.position;
            _batches.Clear();
            var origin = Matrix4x4.TRS(transform.position, Quaternion.identity, Vector3.one);
            foreach (var group in _wanted.GroupBy(w => w.kind))
            {
                if (!types.TryGetValue(group.Key, out var t) || !t.Mesh || !t.Material) { BuildSystem.Log.LogWarning("No game plant " + group.Key); continue; }
                var all = group.Select(w => origin * w.m).ToList();
                for (int i = 0; i < all.Count; i += 1000) _batches.Add((t.Mesh, t.Material, all.GetRange(i, Mathf.Min(1000, all.Count - i))));
            }
            if (!Reveal.Playing) BuildSystem.Log.LogInfo($"Island decor: {_wanted.Count} plants in {_batches.Count} batches, {Pools.Count} tide pools.");
        }

        private void LateUpdate()
        {
            if (!_built || transform.position != _builtAt) Build(); // the reveal raises the island out of the sea
            // Drawn for every camera and its shadows wherever you are: not from out at sea, where 800 plants are specks.
            var cam = GameInfo.CurCamera;
            if (cam && !Reveal.Playing && (cam.transform.position - transform.position).sqrMagnitude > 260f * 260f) return; // (the island's rise is watched from out there)
            foreach (var (mesh, mat, batch) in _batches) Graphics.DrawMeshInstanced(mesh, 0, mat, batch);
        }
    }
}
