using System.Collections;
using System.Collections.Generic;
using FishNet;
using UnityEngine;

namespace HtfFortress
{
    // How a place takes what's thrown into it: the vault's door draws it in with a sparkle, the smithy's anvil with sparks
    // and a puff of forge smoke, the fish stall's counter with a splash.
    public enum Gulp : byte { Vault, Forge, Crate, Absorber }

    // Things going into a chute aren't just gone: the host stills the item (no physics, nothing can grab it, no second
    // trigger) and destroys it a moment later; meanwhile every client hides it and flies a copy of it into the mouth,
    // shrinking and spinning, then puffs.
    public static class Swallow
    {
        public const float Seconds = 0.45f;
        private static readonly HashSet<Item> Going = new HashSet<Item>();
        public static bool IsGoing(Item i) => i && Going.Contains(i);

        // Host: the bookkeeping is done; this is the leaving.
        public static void Server(Item item, Vector3 into, Gulp how)
        {
            if (!item || item.IsDestroying || !Going.Add(item)) return;
            if (item.Rig) { item.Rig.velocity = Vector3.zero; item.Rig.isKinematic = true; }
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = false;
            Fx.Send(FxKind.Absorb, item.transform.position, into, item.ObjectId, (int)how);
            Fx.Instance.StartCoroutine(Gone(item));
        }

        private static IEnumerator Gone(Item item)
        {
            yield return new WaitForSeconds(Seconds);
            Going.Remove(item);
            if (item && !item.IsDestroying) item.DestroyItem();
        }

        // Every client (the host too).
        public static IEnumerator View(FxEvent e)
        {
            var how = (Gulp)e.Arg;
            var cm = InstanceFinder.ClientManager;
            Transform src = cm != null && cm.Objects.Spawned.TryGetValue(e.PieceId, out var nob) && nob ? nob.transform : null;
            var baked = new List<Mesh>();
            var copy = src ? Copy(src, baked) : null;
            if (copy)
            {
                Vector3 from = copy.position, scale = copy.localScale;
                Quaternion turn = copy.rotation;
                for (float t = 0f; t < Seconds && copy; t += Time.deltaTime)
                {
                    float k = t / Seconds, pull = k * k; // slow at first, then in
                    copy.position = Vector3.Lerp(from, e.To, pull) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.3f;
                    copy.rotation = Quaternion.Euler(0f, 720f * k * k, 0f) * turn;
                    copy.localScale = scale * Mathf.Lerp(1f, 0.06f, pull);
                    if (how == Gulp.Vault && Random.value < 0.5f) WeaponFx.Emit(WeaponFx.Sparks("Cyan"), copy.position, 1, Random.onUnitSphere, size: 0.05f, life: 0.3f);
                    if (how == Gulp.Crate && Random.value < 0.4f) WeaponFx.Emit(WeaponFx.Drops, copy.position, 1, Vector3.up);
                    yield return null;
                }
                if (copy) Object.Destroy(copy.gameObject);
            }
            foreach (var m in baked) if (m) Object.Destroy(m);
            Puff(e.To, how);
        }

        private static void Puff(Vector3 at, Gulp how)
        {
            switch (how)
            {
                case Gulp.Vault:
                    WeaponFx.Emit(WeaponFx.Sparks("Cyan"), at, 16, Vector3.up, size: 0.07f);
                    WeaponFx.Emit(WeaponFx.Sparks("Blue"), at, 8, Vector3.up, size: 0.05f);
                    Fx.Sound("Swallow", at, 0.9f); // the game's own gulp
                    break;
                case Gulp.Forge:
                    WeaponFx.Emit(WeaponFx.Sparks("Orange"), at, 22, Vector3.up, size: 0.06f);
                    WeaponFx.Emit(WeaponFx.Dust, at + Vector3.up * 0.2f, 8, Vector3.up);
                    Fx.SoundRandom("MetallicItemImpact_V", 1, 3, at, 0.8f);
                    break;
                case Gulp.Absorber:
                    WeaponFx.Emit(WeaponFx.Sparks("Pink"), at, 14, -Vector3.up, size: 0.05f);
                    WeaponFx.Emit(WeaponFx.Sparks("White"), at, 8, Vector3.up, size: 0.04f);
                    Fx.Sound("Swallow", at, 1f);
                    break;
                default:
                    WeaponFx.Emit(WeaponFx.Drops, at, 18, Vector3.up);
                    WeaponFx.Emit(WeaponFx.Foam, at, 6, Vector3.up);
                    Fx.SoundRandom("ItemHitWaterMedium_V", 1, 3, at, 0.8f);
                    break;
            }
        }

        // What the item looks like right now, as plain meshes (a skinned one baked as it is), under one transform at the
        // item's place; the item's own renderers go dark (it's destroyed in a moment).
        private static Transform Copy(Transform src, List<Mesh> baked)
        {
            var root = new GameObject("Swallowed").transform;
            root.SetPositionAndRotation(src.position, src.rotation);
            int parts = 0;
            foreach (var r in src.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer sk) { mesh = new Mesh(); sk.BakeMesh(mesh); baked.Add(mesh); }
                else if (r.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;
                if (!mesh) continue;
                var part = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                part.transform.localScale = r.transform.lossyScale;
                part.transform.SetParent(root, true);
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                var pr = part.GetComponent<MeshRenderer>();
                pr.sharedMaterials = r.sharedMaterials;
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.enabled = false;
                parts++;
            }
            if (parts > 0) return root;
            Object.Destroy(root.gameObject);
            return null;
        }
    }
}
