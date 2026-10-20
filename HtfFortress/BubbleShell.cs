using System.Collections.Generic;
using UnityEngine;

namespace HtfFortress
{
    // The bubble tower's cover as a soap bubble (Owen: "應該要是薄薄一層像是泡泡，然後多個疊加就只取最外緣"): a film you
    // only see where you look along it (its colours follow the camera every frame: clear face-on, a bright edge in a soap
    // film's pale rainbow, the sun glinting on it), and a thin line on the ground where it ends. Where bubbles overlap,
    // whatever lies inside another one fades out, so a cluster shows only its outside.
    public class BubbleShell : MonoBehaviour
    {
        private static readonly List<BubbleShell> All = new List<BubbleShell>();
        private static Material _mat;
        private const int Around = 64, Rows = 32, LineSteps = 128;
        public const float Tall = 0.8f;          // its height over its radius (most of the lower half is under the ground)
        private const float LineIn = 0.965f;     // the ground line's inner edge, over the radius
        private float _r;
        private Mesh _film, _line;
        private Vector3[] _fv, _fn, _lv;
        private Color32[] _fc, _lc;
        private float[] _la;                     // the ground line's own alpha: bright outside, clear inside
        private Renderer _shown;
        private float _nextLine;
        private readonly List<Vector4> _others = new List<Vector4>(); // the bubbles round it: centre (in this one's frame), radius

        // On `holder` (a child of the tower at its foot), in metres whatever the tower's scale.
        public static void Make(Transform holder, float radius)
        {
            var ls = holder.parent ? holder.parent.lossyScale : Vector3.one;
            holder.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            if (!_mat) _mat = BossVfx.Wash(Color.white);
            holder.gameObject.AddComponent<BubbleShell>().Build(radius);
        }

        private void Build(float r)
        {
            _r = r;
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var t = new List<int>();
            for (int j = 0; j <= Rows; j++)
            {
                float phi = Mathf.PI * (j / (float)Rows - 0.5f);
                for (int i = 0; i <= Around; i++)
                {
                    float th = 2f * Mathf.PI * i / Around;
                    var p = new Vector3(r * Mathf.Cos(phi) * Mathf.Cos(th), r * Tall * Mathf.Sin(phi), r * Mathf.Cos(phi) * Mathf.Sin(th));
                    v.Add(p);
                    nrm.Add(new Vector3(p.x, p.y / (Tall * Tall), p.z).normalized);
                    if (i < Around && j < Rows) { int k = j * (Around + 1) + i; t.AddRange(new[] { k, k + Around + 1, k + 1, k + 1, k + Around + 1, k + Around + 2 }); }
                }
            }
            _fv = v.ToArray();
            _fn = nrm.ToArray();
            _fc = new Color32[_fv.Length];
            _film = new Mesh { name = "FortressBubbleFilm" };
            _film.SetVertices(v);
            _film.colors32 = _fc;
            _film.SetTriangles(t, 0);
            _film.RecalculateBounds();

            v.Clear(); t.Clear();
            var a = new List<float>();
            for (int i = 0; i <= LineSteps; i++)
            {
                float th = 2f * Mathf.PI * i / LineSteps;
                var d = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                v.Add(d * r * LineIn + Vector3.up * 0.12f); a.Add(0f);
                v.Add(d * r + Vector3.up * 0.12f); a.Add(0.55f);
                if (i < LineSteps) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 }); }
            }
            _lv = v.ToArray();
            _la = a.ToArray();
            _lc = new Color32[_lv.Length];
            _line = new Mesh { name = "FortressBubbleLine" };
            _line.SetVertices(v);
            _line.colors32 = _lc;
            _line.SetTriangles(t, 0);
            _line.RecalculateBounds();

            _shown = WeaponFx.Piece(_film, _mat, transform).GetComponent<Renderer>();
            WeaponFx.Piece(_line, _mat, transform);
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);
        private void OnDestroy() { if (_film) Destroy(_film); if (_line) Destroy(_line); }

        private void LateUpdate()
        {
            var cam = GameInfo.CurCamera ? GameInfo.CurCamera : Camera.main; // the game's camera isn't tagged MainCamera: with Camera.main alone nothing ever showed
            if (!cam || _fv == null) return;
            // Recoloured every other frame (every fourth with fewer effects, every eighth from far off), the bubbles taking
            // turns: a fort's worth of towers at 2,000 vertices each, every frame, was a real share of a late wave's frame.
            float near = _r + 40f;
            int every = (cam.transform.position - transform.position).sqrMagnitude > near * near ? 8 : ClientSettings.Reduced ? 4 : 2;
            if ((Time.frameCount + (GetInstanceID() & 0xffff)) % every != 0) return;
            _others.Clear();
            foreach (var o in All)
            {
                if (o == this || o._fv == null) continue;
                Vector3 c = transform.InverseTransformPoint(o.transform.position);
                if (new Vector2(c.x, c.z).sqrMagnitude < (_r + o._r) * (_r + o._r)) _others.Add(new Vector4(c.x, c.y, c.z, o._r));
            }
            if (Time.time >= _nextLine) // the ground line only changes with the bubbles round it
            {
                _nextLine = Time.time + 0.5f;
                for (int k = 0; k < _lv.Length; k++) _lc[k] = new Color32(150, 230, 255, (byte)(255f * _la[k] * Keep(_lv[k])));
                _line.colors32 = _lc;
            }
            if (!_shown.isVisible) return;

            Vector3 eye = transform.InverseTransformPoint(cam.transform.position);
            // From inside it you look at the film face-on everywhere, where a soap film is clear: a little more of it, and
            // its colours swirling, so you still see you're under it.
            bool inside = eye.x * eye.x / (_r * _r) + eye.y * eye.y / (_r * _r * Tall * Tall) + eye.z * eye.z / (_r * _r) < 1f;
            Vector3 sun = RenderSettings.sun ? -transform.InverseTransformDirection(RenderSettings.sun.transform.forward) : Vector3.up;
            float drift = Time.time * 0.9f;
            for (int k = 0; k < _fv.Length; k++)
            {
                Vector3 p = _fv[k];
                float keep = Keep(p);
                if (keep <= 0f) { _fc[k] = default; continue; }
                Vector3 view = (eye - p).normalized, n = _fn[k];
                float facing = Vector3.Dot(n, view), edge = 1f - Mathf.Abs(facing);
                // A soap film's colours: none face-on, a pale rainbow sliding round the edge.
                Color c = Color.HSVToRGB(Mathf.Repeat(0.48f + 0.6f * edge + 0.06f * Mathf.Sin(p.y * 0.6f + drift), 1f), 0.42f * edge, 1f);
                float glint = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(Vector3.Reflect(-view, facing >= 0f ? n : -n), sun)), 24f);
                float swirl = 0.5f + 0.5f * Mathf.Sin(p.y * 1.3f + (p.x - p.z) * 0.5f + drift * 2f);
                if (inside) c = Color.Lerp(c, Color.HSVToRGB(Mathf.Repeat(0.5f + 0.3f * swirl, 1f), 0.35f, 1f), 0.6f);
                float alpha = ((inside ? 0.05f + 0.05f * swirl : 0.02f) + 0.5f * edge * edge * edge + 0.5f * glint) * keep;
                c = Color.Lerp(c, Color.white, glint);
                _fc[k] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(alpha) * 255f));
            }
            _film.colors32 = _fc;
        }

        // How much of a point shows: nothing inside another bubble, fading in over the last 3% to that one's edge.
        private float Keep(Vector3 p)
        {
            float keep = 1f;
            foreach (var o in _others)
            {
                float dx = (p.x - o.x) / o.w, dy = (p.y - o.y) / (o.w * Tall), dz = (p.z - o.z) / o.w;
                float e = dx * dx + dy * dy + dz * dz;
                if (e < 1f) keep = Mathf.Min(keep, Mathf.SmoothStep(0f, 1f, (Mathf.Sqrt(e) - 0.97f) / 0.03f));
            }
            return keep;
        }
    }
}
