using System.Collections.Generic;
using UnityEngine;

namespace HtfFortress
{
    // An overheal repair tower's guard on a building (PieceState.Guard): a pale green film round it, clear face-on and bright
    // at its rim like the bubble tower's (BubbleShell), bands of light climbing it, as strong as the guard is full. It swells
    // in when the guard comes up and thins away when it's gone. A child of the piece: it moves and goes with it.
    public class GuardFilm : MonoBehaviour
    {
        private const int Around = 28, Rows = 14;
        private static Material _mat;
        private Mesh _mesh;
        private Vector3[] _v; // a unit sphere (the transform makes it the building's size): each point is its own normal
        private Color32[] _c;
        private Renderer _shown;
        private float _now, _target, _fitAt;

        // `full`: how full the guard is, 0 to 1 (0: none).
        public static void Set(GameObject piece, float full)
        {
            var t = piece.transform.Find("GuardFilm");
            var f = t ? t.GetComponent<GuardFilm>() : null;
            if (!f)
            {
                if (full <= 0f) return;
                f = new GameObject("GuardFilm").AddComponent<GuardFilm>();
                f.transform.SetParent(piece.transform, false);
                f.Build();
            }
            f._target = Mathf.Clamp01(full);
            f._fitAt = 0f; // fit it again: the building may have been upgraded or moved
        }

        private void Build()
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int j = 0; j <= Rows; j++)
            {
                float phi = Mathf.PI * (j / (float)Rows - 0.5f);
                for (int i = 0; i <= Around; i++)
                {
                    float th = 2f * Mathf.PI * i / Around;
                    v.Add(new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th)));
                    if (i < Around && j < Rows) { int k = j * (Around + 1) + i; tri.AddRange(new[] { k, k + Around + 1, k + 1, k + 1, k + Around + 1, k + Around + 2 }); }
                }
            }
            _v = v.ToArray();
            _c = new Color32[_v.Length];
            _mesh = new Mesh { name = "FortressGuardFilm" };
            _mesh.SetVertices(v);
            _mesh.colors32 = _c;
            _mesh.SetTriangles(tri, 0);
            _mesh.RecalculateBounds();
            if (!_mat) _mat = BossVfx.Wash(Color.white);
            _shown = WeaponFx.Piece(_mesh, _mat, transform).GetComponent<Renderer>();
        }

        // Round the building's look, a little clear of it, its foot under the ground.
        private void Fit()
        {
            var piece = transform.parent;
            var b = BuildSystem.LookBounds(piece.gameObject);
            float w = Mathf.Max(b.extents.x, b.extents.z) * 1.25f + 0.35f, h = b.extents.y * 1.2f + 0.35f;
            var ls = piece.lossyScale;
            transform.position = b.center;
            transform.localScale = new Vector3(w / ls.x, h / ls.y, w / ls.z);
        }

        private void LateUpdate()
        {
            _now = Mathf.MoveTowards(_now, _target, Time.deltaTime * 1.5f);
            if (_now <= 0f && _target <= 0f) { Destroy(gameObject); return; }
            if (Time.time >= _fitAt) { _fitAt = Time.time + 1f; Fit(); }
            var cam = GameInfo.CurCamera ? GameInfo.CurCamera : Camera.main;
            if (!cam || !_shown.isVisible || (Time.frameCount + (GetInstanceID() & 0xffff)) % (ClientSettings.Reduced ? 4 : 2) != 0) return;
            Vector3 eye = transform.InverseTransformPoint(cam.transform.position);
            bool inside = eye.sqrMagnitude < 1f;
            float time = Time.time;
            for (int k = 0; k < _v.Length; k++)
            {
                Vector3 p = _v[k];
                float edge = inside ? 0f : 1f - Mathf.Abs(Vector3.Dot(p, (eye - p).normalized));
                float band = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(p.y * 6f - time * 2.4f), 6f); // light climbing it
                float a = (0.03f + 0.55f * edge * edge * edge + 0.12f * band * (0.3f + edge)) * _now;
                Color c = Color.Lerp(new Color(0.5f, 1f, 0.62f), Color.white, 0.3f + 0.5f * edge * edge);
                _c[k] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(a) * 255f));
            }
            _mesh.colors32 = _c;
        }

        private void OnDestroy() { if (_mesh) Destroy(_mesh); }
    }
}
