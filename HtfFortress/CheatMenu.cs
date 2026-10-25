using System;
using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HtfFortress
{
    // The clickable test/cheat menu (Debug / DebugKeys): the hotkeys as buttons, plus the currencies a tester should not have to
    // grind for, and a size control (Owen: "設計的好用一點，功能面還有要能縮放"). Host actions go through FortressPlugin.TestKey
    // so a friend with the host's test keys on can use it too; cheats that move the save are host-only.
    public class CheatMenu : MonoBehaviour
    {
        public static CheatMenu Instance;
        public static bool IsOpen { get; private set; }

        private Canvas _canvas;
        private RectTransform _panel;
        private TextMeshProUGUI _godText, _freezeText, _roundText, _scaleText;
        private float _nextRefresh;

        private void Awake() => Instance = this;

        private static FortressPlugin P => FortressPlugin.Instance;

        private static bool KeysOn => FortressPlugin.Instance != null &&
            (FortressPlugin.Instance.TestKeysOn || (!InstanceFinder.IsServerStarted && Siege.Instance && Siege.Instance.View.TestKeys));

        private void Update()
        {
            if (!KeysOn)
            {
                if (IsOpen) Close();
                return;
            }
            var kb = Keyboard.current;
            if (kb == null || ChatManager.IsTyping) return;
            if (kb[P.MenuKey].wasPressedThisFrame) { Toggle(); return; }
            if (!IsOpen) return;
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            if (!Hud.Font || P == null) return;
            if (!_panel) Build();
            if (!_panel) return;
            IsOpen = true;
            Hud.Canvas("FortressCheat", 620, ref _canvas).gameObject.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_canvas) _canvas.gameObject.SetActive(false);
        }

        private void Build()
        {
            if (!EventSystem.current)
            {
                var es = new GameObject("FortressCheatEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            var canvasGo = Hud.Canvas("FortressCheat", 620, ref _canvas).gameObject;
            DontDestroyOnLoad(canvasGo);
            var img = Hud.Box(canvasGo.transform, new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(600f, 1010f), Hud.PanelSolid, 18f, pivot: new Vector2(1f, 1f));
            img.raycastTarget = true;
            _panel = img.rectTransform;

            var title = Hud.Label(_panel, L10n.T("cheatTitle"), 28f, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(560f, 36f), TextAlignmentOptions.Center, bold: true);
            title.color = Hud.Accent;
            Hud.Box(_panel, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(70f, 3f), Hud.Accent, 1.5f);

            const float X0 = 16f, X1 = 300f, W = 264f, H = 44f, DY = 52f;
            float y = -84f;
            int row = 0;
            Btn(X0, y - DY * row, L10n.T("cheatStart"), () => P.TestKey(P.ToggleKey, false), out _roundText);
            Btn(X1, y - DY * row++, L10n.T("cheatCoins"), () => P.TestKey(Key.F5, false), out _);
            Btn(X0, y - DY * row, L10n.T("cheatCoinsBig"), () => P.TestKey(Key.F5, true), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatCoinsHuge"), () => P.TestKey(Key.F5, false, true), out _);
            Btn(X0, y - DY * row, L10n.T("cheatGodOn"), () => P.TestKey(Key.F6, false), out _godText);
            Btn(X1, y - DY * row++, L10n.T("cheatKill"), () => P.TestKey(Key.F7, false), out _);
            Btn(X0, y - DY * row, L10n.T("cheatHorde50"), () => P.DebugSpawnHorde(50), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatHorde200"), () => P.DebugSpawnHorde(200), out _);
            Btn(X0, y - DY * row, L10n.T("cheatRepair"), () => P.TestKey(Key.F3, false), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatBoss"), () => P.TestKey(Key.F1, false), out _);
            Btn(X0, y - DY * row, L10n.T("cheatBossHurt"), () => P.TestKey(Key.F2, false), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatFreezeOn"), () => P.TestKey(Key.F11, true), out _freezeText);
            Btn(X0, y - DY * row, L10n.T("cheatNextWave"), () => P.TestKey(Key.F8, false), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatJump5"), () => P.TestKey(Key.F8, true), out _);
            Btn(X0, y - DY * row, L10n.T("cheatTeleport"), () => P.TestKey(Key.F4, false), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatIntro"), () => P.TestKey(Key.F10, false), out _);

            Btn(X0, y - DY * row, L10n.T("cheatSouls"), () => HostOnly(() => P.DebugSouls(100)), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatScales"), () => HostOnly(() => P.DebugScales(1000)), out _);
            Btn(X0, y - DY * row, L10n.T("cheatGear"), () => HostOnly(() => P.DebugGear(5)), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatUnlock"), () => HostOnly(() => P.DebugUnlockAll()), out _);
            // The modifier combos, as buttons: Ctrl is awkward on some keyboards (and Mac needs Fn), so nothing here needs it.
            Btn(X0, y - DY * row, L10n.T("cheatClearGround"), () => P.TestKey(Key.F7, true), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatBreakMove"), () => P.TestKey(Key.F2, true), out _);
            Btn(X0, y - DY * row, L10n.T("cheatTrophies"), () => P.TestKey(Key.F3, true), out _);
            Btn(X1, y - DY * row++, L10n.T("cheatJumpTen"), () => P.TestKey(Key.F8, false, true), out _);

            float sy = y - DY * row - 8f;
            Btn(X0, sy, L10n.T("cheatScaleMinus"), () => { P.MenuScale -= 0.1f; Refresh(); }, out _, 120f);
            Btn(X1 + 144f, sy, L10n.T("cheatScalePlus"), () => { P.MenuScale += 0.1f; Refresh(); }, out _, 120f);
            _scaleText = Hud.Label(_panel, "", 20f, new Vector2(0f, 1f), new Vector2(X0 + 130f, sy - 8f), new Vector2(144f, 30f), TextAlignmentOptions.Center);

            Hud.Label(_panel, L10n.T("cheatClose"), 16f, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(560f, 22f), TextAlignmentOptions.Center).color = Hud.Muted;

            void Btn(float x, float yy, string text, Action act, out TextMeshProUGUI label, float width = W)
            {
                var b = Hud.Box(_panel, new Vector2(0f, 1f), new Vector2(x, yy), new Vector2(width, H), new Color(0.13f, 0.16f, 0.22f, 1f), 10f);
                b.raycastTarget = true;
                var btn = b.gameObject.AddComponent<Button>();
                var c = btn.colors; c.highlightedColor = new Color(1.3f, 1.3f, 1.3f); c.pressedColor = new Color(0.8f, 0.8f, 0.8f); btn.colors = c;
                btn.onClick.AddListener(() => { Fx.Sound("Click", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.5f, AudioDistance.Short); act(); });
                label = Hud.Label(b.transform, text, 20f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width - 12f, H - 6f), TextAlignmentOptions.Center, bold: true);
                label.enableAutoSizing = true; label.fontSizeMin = 12f; label.fontSizeMax = 20f;
            }
        }

        private void HostOnly(Action act)
        {
            if (!InstanceFinder.IsServerStarted) { Hud.Banner(L10n.T("cheatHostOnly"), 2f, Hud.Bad); return; }
            act();
        }

        private void Refresh()
        {
            if (!_panel || P == null) return;
            if (_roundText) _roundText.text = L10n.T(P.Running ? "cheatStop" : "cheatStart");
            if (_godText) _godText.text = L10n.T(PlayerManager.InGodMode ? "cheatGodOff" : "cheatGodOn");
            if (_freezeText) _freezeText.text = L10n.T(FortressPlugin.Frozen ? "cheatFreezeOff" : "cheatFreezeOn");
            if (_scaleText) _scaleText.text = L10n.T("cheatScale", Mathf.RoundToInt(P.MenuScale * 100f));
            _panel.localScale = Vector3.one * P.MenuScale;
        }
    }
}
