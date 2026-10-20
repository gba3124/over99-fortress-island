using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    // Screen UI built from the game's own pieces: its rounded 9-sliced panel sprite ("Ui"), icons (Skull, Heart,
    // Arrow...) and TMP font (its fallbacks cover Chinese; IMGUI fonts don't under Wine). Everything is laid out on a
    // 1920x1080 reference canvas so it scales with the window.
    public static class Hud
    {
        public static readonly Color Panel = new Color(0.05f, 0.07f, 0.10f, 0.72f);
        public static readonly Color PanelSolid = new Color(0.07f, 0.09f, 0.12f, 1f);   // modal panels: fully opaque (linear blending shows any see-through)
        public static readonly Color Raised = new Color(0.12f, 0.145f, 0.19f, 1f);     // a section on a modal panel
        public static readonly Color Accent = new Color(1f, 0.69f, 0.27f);   // warm orange, like the game's tutorial box
        public static readonly Color Gold = new Color(1f, 0.84f, 0.35f);
        public static readonly Color Good = new Color(0.45f, 0.86f, 0.52f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.36f);
        public static readonly Color Muted = new Color(0.66f, 0.72f, 0.80f);
        public static readonly Color Ink = new Color(0.96f, 0.97f, 1f);

        private static Canvas _canvas;
        public static Transform Root => Canvas("FortressHUD", 500, ref _canvas).transform;

        public static Canvas Canvas(string name, int order, ref Canvas cache)
        {
            if (cache) return cache;
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(go);
            cache = go.GetComponent<Canvas>();
            cache.renderMode = RenderMode.ScreenSpaceOverlay;
            cache.sortingOrder = order;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;
            if (name == "FortressHUD") go.AddComponent<HudRunner>();
            return cache;
        }

        // A mouse menu is up (vault, piece menu, the help panel): the game's own input is held and the cursor is free.
        public static bool ModalOpen => MenuOpen || Tips.HelpShown;
        public static bool MenuOpen => VaultUI.IsOpen || PieceMenu.IsOpen || StallMenu.IsOpen || Reveal.Playing; // any but the help (it gives way to these)

        public static TMP_FontAsset Font => AccessTools.Field(typeof(LocalizationManager), "_defaultFontAsset")?.GetValue(
            AccessTools.Field(typeof(LocalizationManager), "_instance")?.GetValue(null)) as TMP_FontAsset;

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        public static Sprite Sprite(string name)
        {
            if (Sprites.TryGetValue(name, out var s) && s) return s;
            s = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(x => x.name == name);
            if (s) Sprites[name] = s;
            return s;
        }

        // The mod's own pictures: Icons/<name>.png next to the plugin (gear, weapons, pieces; tools/make_icons.py renders them
        // from the models). Missing ones come back null, and the caller shows its sign instead.
        public static string IconDir;
        private static readonly Dictionary<string, Sprite> Pictures = new Dictionary<string, Sprite>();

        public static Sprite PictureOf(string name)
        {
            if (Pictures.TryGetValue(name, out var s)) return s;
            // The game's guns have no picture of ours: the one ItemIcons takes of the game's own gun (none for a frame or two).
            if (name.StartsWith("Gear") && System.Enum.TryParse(name.Substring(4), out Gear g) && GearCatalog.IsGameGun(g))
                return ItemIcons.Of(WeaponItems.GameId(g));
            string path = System.IO.Path.Combine(IconDir ?? "", name + ".png");
            if (System.IO.File.Exists(path))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                if (ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(path), true))
                    s = UnityEngine.Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            Pictures[name] = s;
            return s;
        }

        // A picture filling `size` (its shape kept); null if there is no such picture.
        public static Image Picture(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var sp = name != null ? PictureOf(name) : null;
            if (!sp) return null;
            var img = PictureSlot(parent, anchor, pos, size, pivot);
            img.sprite = sp;
            img.enabled = true;
            return img;
        }

        // An empty picture frame, for pictures that change (ShowPicture).
        public static Image PictureSlot(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var img = Node(parent, "Picture", anchor, pos, size, pivot).gameObject.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        public static void ShowPicture(Image img, string name)
        {
            var sp = name != null ? PictureOf(name) : null;
            if (img.sprite != sp) img.sprite = sp;
            img.enabled = sp;
        }

        // ---------- building blocks ----------

        public static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // A rounded panel (the game's "Ui" sprite, 9-sliced) with `radius` corners in reference pixels.
        public static Image Box(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, float radius = 14f, string sprite = "Ui", Vector2? pivot = null)
        {
            var rt = Node(parent, "Box", anchor, pos, size, pivot);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            Round(img, radius, sprite);
            return img;
        }

        public static void Round(Image img, float radius, string sprite = "Ui")
        {
            var sp = radius > 0f ? Sprite(sprite) : null;
            if (!sp) return;
            img.sprite = sp;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = Mathf.Max(0.01f, sp.border.x * 100f / (sp.pixelsPerUnit * radius));
        }

        public static Image Icon(Transform parent, string sprite, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Node(parent, "Icon", anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Sprite(sprite);
            img.preserveAspect = true;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Vector2 anchor, Vector2 pos, Vector2 box, TextAlignmentOptions align,
                                            Color? color = null, bool bold = false, bool outline = false, Vector2? pivot = null)
        {
            var rt = Node(parent, "Label", anchor, pos, box, pivot);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? Ink;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            t.richText = true;
            // TextMeshPro throws setting an outline on a label that isn't showing yet (the HUD is hidden while a menu is open: a
            // helper's or a fish's name tag made then): it goes on when the label first shows.
            if (outline) { if (rt.gameObject.activeInHierarchy) Outline(t); else rt.gameObject.AddComponent<OutlineOnShow>(); }
            t.text = text;
            rt.gameObject.AddComponent<OneLine>();
            return t;
        }

        private static void Outline(TextMeshProUGUI t) { t.outlineWidth = 0.18f; t.outlineColor = new Color(0f, 0f, 0f, 0.85f); }
        private class OutlineOnShow : MonoBehaviour { private void OnEnable() { Outline(GetComponent<TextMeshProUGUI>()); Destroy(this); } }

        // A label whose box is under two lines tall never wraps (Owen: "很多地方的文字都會換行"): text wider than its box
        // shrinks to fit, down to 60% of its size (past that it runs over, still on one line). Taller boxes are paragraphs
        // and wrap as before; labels their owner auto-sizes are left alone. Looked at when the text, the box or the size its
        // owner gives it changes.
        internal class OneLine : MonoBehaviour
        {
            private TextMeshProUGUI _t;
            private string _text;
            private float _full, _set = -1f;
            private Vector2 _box;

            private void LateUpdate()
            {
                if (!_t && !(_t = GetComponent<TextMeshProUGUI>())) return;
                if (_t.enableAutoSizing) return;
                if (_t.fontSize != _set) _full = _t.fontSize; // its owner sized it
                var box = _t.rectTransform.rect.size;
                if (_t.fontSize == _set && box == _box && _t.text == _text) return;
                _text = _t.text;
                _box = box;
                _set = _t.fontSize;
                if (box.y >= _full * 2f || box.x < 1f || string.IsNullOrEmpty(_text)) return;
                _t.enableWordWrapping = false;
                _t.fontSize = _full;
                float need = _t.GetPreferredValues(_text, float.PositiveInfinity, float.PositiveInfinity).x;
                _t.fontSize = _set = need > box.x ? Mathf.Max(_full * 0.6f, _full * box.x / need) : _full;
            }
        }

        // A rounded bar; Set(0..1) moves the fill.
        public class Bar
        {
            public Image Back, Fill;
            private RectTransform _fill;

            public void Set(float v, Color? c = null)
            {
                _fill.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
                _fill.gameObject.SetActive(v > 0.001f);
                if (c.HasValue) Fill.color = c.Value;
            }

            public static Bar Make(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, Vector2? pivot = null)
            {
                var b = new Bar { Back = Box(parent, anchor, pos, size, new Color(0f, 0f, 0f, 0.45f), size.y / 2f, pivot: pivot) };
                b.Fill = Box(b.Back.transform, Vector2.zero, Vector2.zero, Vector2.zero, fill, size.y / 2f);
                b._fill = b.Fill.rectTransform;
                b._fill.anchorMin = Vector2.zero;
                b._fill.anchorMax = Vector2.one;
                b._fill.pivot = new Vector2(0f, 0.5f);
                b._fill.sizeDelta = Vector2.zero;
                return b;
            }
        }

        public static Color HealthColor(float f) => f > 0.6f ? Good : f > 0.3f ? Gold : Bad;

        // "[U] upgrade" -> the key drawn as a little key cap. Only for hint strings (rarity labels use brackets too).
        private static readonly Regex KeyRx = new Regex(@"\[([^\]\[]{1,14})\]");
        public static string Keys(string s) => KeyRx.Replace(s ?? "", m => $"<mark=#FFFFFF2E padding=\"8,8,3,3\"><b> {m.Groups[1].Value} </b></mark>");

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // Plain outlined text straight on the screen (for the few texts without a panel).
        public static TextMeshProUGUI CreateText(string name, Vector2 anchor, Vector2 offset, float size, TextAlignmentOptions align)
        {
            if (!Font) return null;
            var t = Label(Root, "", size, anchor, offset, new Vector2(1400f, 60f), align, outline: true);
            t.gameObject.name = name;
            return t;
        }

        // The game's arrow icon (screen-edge enemy markers).
        public static Image CreateMarker(string name, Color color, float size)
        {
            var img = Icon(Root, "Arrow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), color);
            img.gameObject.name = name;
            return img;
        }

        // ---------- prompt (one at a time, lower middle) and banner ----------

        private static Image _promptBox;
        private static TextMeshProUGUI _promptText;
        private static int _promptFrame = -10;

        // Call every frame while the prompt should show, e.g. "[U] Open the vault".
        public static void Prompt(string text)
        {
            if (!Font) return;
            if (!_promptBox)
            {
                _promptBox = Box(Root, new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(400f, 54f), Panel, 27f);
                _promptText = Label(_promptBox.transform, "", 26f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 54f), TextAlignmentOptions.Center);
            }
            string k = Keys(text);
            if (_promptText.text != k)
            {
                _promptText.text = k;
                _promptBox.rectTransform.sizeDelta = new Vector2(_promptText.GetPreferredValues(k).x + 56f, 54f);
            }
            _promptFrame = Time.frameCount;
        }

        private static Image _banner, _bannerLine;
        private static TextMeshProUGUI _bannerText;
        private static CanvasGroup _bannerGroup;
        private static float _bannerStart, _bannerUntil;

        public static void Banner(string text, float seconds, Color? accent = null)
        {
            if (!Font) return;
            if (!_banner)
            {
                _banner = Box(Root, new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(700f, 104f), Panel, 22f);
                _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
                _bannerGroup.blocksRaycasts = false;
                _bannerLine = Box(_banner.transform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(120f, 5f), Accent, 2.5f);
                _bannerText = Label(_banner.transform, "", 50f, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(1500f, 100f), TextAlignmentOptions.Center, bold: true);
            }
            _bannerText.text = text;
            _bannerLine.color = accent ?? Accent;
            Vector2 pref = _bannerText.GetPreferredValues(text);
            _banner.rectTransform.sizeDelta = new Vector2(Mathf.Max(420f, pref.x + 140f), Mathf.Max(104f, pref.y + 44f));
            _bannerText.rectTransform.sizeDelta = new Vector2(1500f, pref.y + 10f);
            _bannerStart = Time.time;
            _bannerUntil = Time.time + seconds;
        }

        public static void HideBanner() => _bannerUntil = 0f;

        // A progress ring around the crosshair with a caption (building, removing). Call every frame while it shows.
        private static Image _ringBack, _ringFill;
        private static TextMeshProUGUI _ringText;
        private static int _ringFrame = -10;

        public static void Ring(float progress, string caption, Color? color = null)
        {
            if (!Font) return;
            if (!_ringBack)
            {
                _ringBack = Icon(Root, "UICircle_outline", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 92f), new Color(0f, 0f, 0f, 0.45f));
                _ringFill = Icon(_ringBack.transform, "UICircle_outline", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 92f), Accent);
                _ringFill.type = Image.Type.Filled;
                _ringFill.fillMethod = Image.FillMethod.Radial360;
                _ringFill.fillOrigin = (int)Image.Origin360.Top;
                _ringFill.fillClockwise = true;
                _ringText = Label(_ringBack.transform, "", 22f, new Vector2(0.5f, 0f), new Vector2(0f, -36f), new Vector2(600f, 32f), TextAlignmentOptions.Center, bold: true, outline: true);
            }
            _ringFill.fillAmount = Mathf.Clamp01(progress);
            _ringFill.color = color ?? Accent;
            _ringText.text = caption;
            _ringFrame = Time.frameCount;
        }

        // Drives the prompt and the banner's pop-in / fade-out.
        internal class HudRunner : MonoBehaviour
        {
            private void LateUpdate()
            {
                if (_promptBox) _promptBox.gameObject.SetActive(Time.frameCount - _promptFrame <= 1);
                if (_ringBack) _ringBack.gameObject.SetActive(Time.frameCount - _ringFrame <= 1);
                if (!_banner) return;
                float t = Time.time;
                bool on = t < _bannerUntil;
                _banner.gameObject.SetActive(on);
                if (!on) return;
                float a = Mathf.Clamp01((t - _bannerStart) / 0.15f), z = Mathf.Clamp01((_bannerUntil - t) / 0.4f);
                _banner.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, 1f - (1f - a) * (1f - a));
                _bannerGroup.alpha = Mathf.Min(a, z);
            }
        }
    }
}
