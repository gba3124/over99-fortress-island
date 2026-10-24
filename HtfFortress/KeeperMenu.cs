using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HtfFortress
{
    // The lighthouse keeper (Owen: "消耗魂跟燈塔旁的阿伯直接跳關"): souls earned by clearing wave 100 and going past it can be
    // spent here to skip waves, ten at a time, never past the save's own record. Small cost (one soul a skip), so it is a
    // convenience for a run, not a wall. Host-only: it moves the run's wave.
    public class KeeperMenu : MonoBehaviour
    {
        public static KeeperMenu Instance;
        public static bool IsOpen { get; private set; }

        private Canvas _canvas;
        private RectTransform _panel;
        private TextMeshProUGUI _souls, _best, _now, _hint, _skipText;
        private Button _skip;
        private readonly TextMeshProUGUI[] _upName = new TextMeshProUGUI[5], _upCost = new TextMeshProUGUI[5];
        private readonly Button[] _upBtn = new Button[5];
        private float _nextRefresh;

        private void Awake() => Instance = this;

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (!Hud.Font) return;
            if (!_panel) Build();
            if (!_panel) return;
            IsOpen = true;
            Hud.Canvas("FortressKeeper", 612, ref _canvas).gameObject.SetActive(true);
            PlayerCamera.ToggleMouse(true);
            AutoTest.Count("keeper_opened");
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_canvas) _canvas.gameObject.SetActive(false);
            PlayerCamera.ToggleMouse(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (Player.LocalPlayer && FortressIsland.Instance && Player.LocalPlayer.Transform &&
                (Player.LocalPlayer.Transform.position - FortressIsland.Instance.LighthousePos).sqrMagnitude > 9f * 9f) { Close(); return; }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void Build()
        {
            if (!EventSystem.current)
            {
                var es = new GameObject("FortressKeeperEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            var canvasGo = Hud.Canvas("FortressKeeper", 612, ref _canvas).gameObject;
            DontDestroyOnLoad(canvasGo);
            var dim = Hud.Box(canvasGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.4f), 0f);
            dim.raycastTarget = true;
            _panel = Hud.Box(canvasGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 800f), Hud.PanelSolid, 24f).rectTransform;

            var t = new Vector2(0.5f, 1f);
            var title = Hud.Label(_panel, L10n.T("keeperTitle"), 34f, t, new Vector2(0f, -26f), new Vector2(760f, 44f), TextAlignmentOptions.Center, bold: true);
            title.color = Hud.Accent;
            Hud.Box(_panel, new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(80f, 4f), Hud.Accent, 2f);
            _souls = Hud.Label(_panel, "", 26f, t, new Vector2(-200f, -104f), new Vector2(360f, 36f), TextAlignmentOptions.Left);
            _best = Hud.Label(_panel, "", 22f, t, new Vector2(190f, -108f), new Vector2(360f, 30f), TextAlignmentOptions.Right);
            _now = Hud.Label(_panel, "", 20f, t, new Vector2(0f, -150f), new Vector2(720f, 30f), TextAlignmentOptions.Center);
            _now.color = Hud.Muted;
            // The permanent tree: five lines, each level bought with souls and kept for good.
            for (int i = 0; i < 5; i++)
            {
                int kind = i;
                float y = -176f - 62f * i;
                _upName[i] = Hud.Label(_panel, "", 22f, new Vector2(0f, 1f), new Vector2(36f, y), new Vector2(520f, 40f), TextAlignmentOptions.Left);
                _upCost[i] = Hud.Label(_panel, "", 20f, new Vector2(1f, 1f), new Vector2(-210f, y - 8f), new Vector2(200f, 32f), TextAlignmentOptions.Right);
                _upCost[i].color = Hud.Muted;
                _upBtn[i] = Btn(_panel, new Vector2(1f, 1f), new Vector2(-90f, y), new Vector2(140f, 46f), new Color(0.26f, 0.42f, 0.78f),
                    () => { if (!InstanceFinder.IsServerStarted) { Hud.Banner(L10n.T("keeperHostOnly"), 2f, Hud.Bad); return; } if (Loot.Instance && Loot.Instance.ServerBuySoulUpgrade(kind)) Refresh(); }, out var bt);
                bt.text = L10n.T("soulUpBuy"); bt.fontSize = 20f;
            }
            _skip = Btn(_panel, new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(420f, 70f), new Color(0.26f, 0.42f, 0.78f),
                () => { if (FortressPlugin.Instance && FortressPlugin.Instance.ServerSkipTenWaves()) Close(); }, out _skipText);
            _hint = Hud.Label(_panel, "", 18f, new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(820f, 30f), TextAlignmentOptions.Center);
            _hint.color = Hud.Muted;
            Hud.Label(_panel, L10n.T("keeperClose"), 16f, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(720f, 24f), TextAlignmentOptions.Center).color = Hud.Muted;
        }

        private void Refresh()
        {
            if (!_panel) return;
            bool host = InstanceFinder.IsServerStarted;
            var v = Loot.Instance ? (host ? Loot.Instance.Vault : Loot.Instance.ClientVault) : null;
            int wave = host ? (FortressPlugin.Instance ? FortressPlugin.Instance.WaveNow : 0) : (Siege.Instance ? Siege.Instance.View.Wave : 0);
            int best = v != null ? v.Best : 0;
            int souls = v != null ? v.Souls : 0;
            int target = (wave / 10 + 1) * 10;
            int cap = best / 10 * 10;
            bool inBuild = FortressPlugin.Instance != null && FortressPlugin.Instance.InBuildTime;
            bool can = host && inBuild && v != null && best >= 100 && souls >= 1 && target <= cap;
            _souls.text = $"<color=#7fd0ff>◆</color> {L10n.T("keeperSouls", souls)}";
            _best.text = L10n.T("keeperBest", best);
            _now.text = L10n.T("keeperNow", wave);
            for (int i = 0; i < 5; i++)
            {
                int lvl = v != null ? Loot.SoulLevel(v, i) : 0, cost = Loot.SoulUpgradeCost(i, lvl);
                _upName[i].text = L10n.T("soulUp" + new[] { "Dmg", "Crit", "Coins", "Scales", "Core" }[i]);
                _upCost[i].text = L10n.T("soulUpLevel", lvl, cost);
                _upBtn[i].interactable = host && v != null && souls >= cost;
            }
            _skipText.text = L10n.T("keeperSkipBtn", target);
            _skip.interactable = can;
            _hint.text = !host ? L10n.T("keeperHostOnly")
                : v == null ? ""
                : best < 100 ? L10n.T("keeperNeed100")
                : target > cap ? L10n.T("keeperAtRecord", cap)
                : souls < 1 ? L10n.T("keeperNoSouls")
                : !inBuild ? L10n.T("keeperInFight")
                : L10n.T("keeperSkipCost", souls);
        }

        private static Button Btn(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c, System.Action onClick, out TextMeshProUGUI label)
        {
            var img = Hud.Box(parent, anchor, pos, size, c, 14f);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            b.colors = colors;
            b.onClick.AddListener(() =>
            {
                Fx.Sound("Click", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.6f, AudioDistance.Short);
                onClick();
            });
            label = Hud.Label(img.transform, "", 24f, new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(12f, 6f), TextAlignmentOptions.Center, bold: true);
            label.enableAutoSizing = true; label.fontSizeMin = 15f; label.fontSizeMax = 24f;
            return b;
        }
    }
}
