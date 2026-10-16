using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HtfFortress
{
    // Screen text drawn with the game's own TMP font (its fallbacks cover Chinese; IMGUI fonts don't under Wine).
    public static class Hud
    {
        private static Canvas _canvas;

        public static TextMeshProUGUI CreateText(string name, Vector2 anchor, Vector2 offset, float size, TextAlignmentOptions align)
        {
            var font = AccessTools.Field(typeof(LocalizationManager), "_defaultFontAsset")?.GetValue(
                AccessTools.Field(typeof(LocalizationManager), "_instance")?.GetValue(null)) as TMP_FontAsset;
            if (!font) return null;

            if (!_canvas)
            {
                var canvasGo = new GameObject("FortressHUD");
                Object.DontDestroyOnLoad(canvasGo);
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 500;
            }

            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = Color.black;
            tmp.raycastTarget = false;
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(1400f, 60f);
            return tmp;
        }
    }
}
