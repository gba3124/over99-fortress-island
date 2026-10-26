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
        private UnityEngine.UI.Image _soulIcon;
        private Button _skip;
        private readonly TextMeshProUGUI[] _upName = new TextMeshProUGUI[9], _upCost = new TextMeshProUGUI[9], _upEffect = new TextMeshProUGUI[9];
        private readonly Button[] _upBtn = new Button[9];
        private readonly UnityEngine.UI.Image[] _upRow = new UnityEngine.UI.Image[9];
        private readonly Button[] _tabBtn = new Button[3];
        private readonly TextMeshProUGUI[] _tabText = new TextMeshProUGUI[3];
        private int _soulTab;
        private float _nextRefresh, _buyLockUntil;

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
            if (VaultUI.IsOpen && VaultUI.Instance) VaultUI.Instance.Close();
            if (StallMenu.IsOpen && StallMenu.Instance) StallMenu.Instance.Close();
            if (PieceMenu.IsOpen && PieceMenu.Instance) PieceMenu.Instance.Close();
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
            // Keep the cursor free while the keeper screen is open: a click must never lock the mouse back to the centre.
            if (Cursor.lockState != CursorLockMode.None) PlayerCamera.ToggleMouse(true);
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
            _panel = Hud.Box(canvasGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(840f, 980f), Hud.PanelSolid, 26f).rectTransform;

            var t = new Vector2(0.5f, 1f);
            var title = Hud.Label(_panel, L10n.T("keeperTitle"), 34f, t, new Vector2(0f, -24f), new Vector2(760f, 44f), TextAlignmentOptions.Center, bold: true);
            title.color = Hud.Accent;
            Hud.Box(_panel, new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(84f, 4f), Hud.Accent, 2f);

            // One aligned header row: souls on the left, record on the right, this run in the middle.
            _souls = Hud.Label(_panel, "", 25f, new Vector2(0f, 1f), new Vector2(54f, -92f), new Vector2(300f, 34f), TextAlignmentOptions.Left, bold: true);
            _soulIcon = Hud.Picture(_panel, "SoulCurrency", new Vector2(0f, 1f), new Vector2(16f, -92f), new Vector2(30f, 30f), pivot: new Vector2(0f, 1f));
            _best = Hud.Label(_panel, "", 21f, new Vector2(1f, 1f), new Vector2(-42f, -94f), new Vector2(300f, 30f), TextAlignmentOptions.Right);
            _best.color = Hud.Muted;
            _now = Hud.Label(_panel, "", 20f, t, new Vector2(0f, -132f), new Vector2(760f, 28f), TextAlignmentOptions.Center);
            _now.color = Hud.Muted;
            Hud.Box(_panel, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(760f, 2f), new Color(1f, 1f, 1f, 0.08f), 0f);

            // Three tabs: character / towers / resources. Every line is still one row, but the tree is no longer a wall of nine.
            for (int i = 0; i < 3; i++)
            {
                int tab = i;
                _tabBtn[i] = Btn(_panel, new Vector2(0.5f, 1f), new Vector2((i - 1) * 210f, -182f), new Vector2(190f, 38f), new Color(0.2f, 0.25f, 0.34f),
                    () => { _soulTab = tab; Refresh(); }, out var tt);
                _tabText[i] = tt; tt.text = L10n.T(new[] { "soulTabCharacter", "soulTabTowers", "soulTabResources" }[i]); tt.fontSize = 19f;
            }
            for (int i = 0; i < 9; i++)
            {
                int kind = i;
                float y = -238f;
                _upRow[i] = Hud.Box(_panel, new Vector2(0f, 1f), new Vector2(34f, y), new Vector2(772f, 60f), i % 2 == 0 ? new Color(1f, 1f, 1f, 0.035f) : new Color(0f, 0f, 0f, 0.10f), 10f);
                _upName[i] = Hud.Label(_panel, "", 22f, new Vector2(0f, 1f), new Vector2(52f, y - 7f), new Vector2(420f, 28f), TextAlignmentOptions.Left);
                _upEffect[i] = Hud.Label(_panel, "", 15f, new Vector2(0f, 1f), new Vector2(52f, y - 33f), new Vector2(420f, 20f), TextAlignmentOptions.Left, Hud.Gold);
                _upCost[i] = Hud.Label(_panel, "", 19f, new Vector2(1f, 1f), new Vector2(-224f, y - 15f), new Vector2(190f, 30f), TextAlignmentOptions.Right);
                _upCost[i].color = Hud.Muted;
                _upBtn[i] = Btn(_panel, new Vector2(1f, 1f), new Vector2(-34f, y - 7f), new Vector2(150f, 46f), new Color(0.26f, 0.42f, 0.78f),
                    () =>
                    {
                        if (Time.unscaledTime < _buyLockUntil) return;
                        _buyLockUntil = Time.unscaledTime + 0.08f;
                        if (!InstanceFinder.IsServerStarted) { Hud.Banner(L10n.T("keeperHostOnly"), 2f, Hud.Bad); return; }
                        if (Loot.Instance && Loot.Instance.ServerBuySoulUpgrade(kind)) Refresh();
                    }, out var bt);
                bt.text = L10n.T("soulUpBuy"); bt.fontSize = 20f;
            }

            // The wave skip sits below the tree with its own label and reason line.
            _skip = Btn(_panel, new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(420f, 64f), new Color(0.26f, 0.42f, 0.78f),
                () => { if (FortressPlugin.Instance && FortressPlugin.Instance.ServerSkipTenWaves()) Close(); }, out _skipText);
            _hint = Hud.Label(_panel, "", 18f, new Vector2(0.5f, 0f), new Vector2(0f, 66f), new Vector2(780f, 30f), TextAlignmentOptions.Center);
            _hint.color = Hud.Muted;
            Hud.Label(_panel, L10n.T("keeperClose"), 16f, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(720f, 24f), TextAlignmentOptions.Center).color = Hud.Muted;
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
            _souls.text = L10n.T("keeperSouls", souls);
            _best.text = L10n.T("keeperBest", best);
            _now.text = L10n.T("keeperNow", wave);
            var shown = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 9; i++) if (Loot.SoulCategory(i) == _soulTab) shown.Add(i);
            for (int i = 0; i < 9; i++)
            {
                bool on = shown.Contains(i);
                _upRow[i].gameObject.SetActive(on);
                _upName[i].gameObject.SetActive(on); _upEffect[i].gameObject.SetActive(on); _upCost[i].gameObject.SetActive(on); _upBtn[i].gameObject.SetActive(on);
                if (!on) continue;
                float y = -238f - 70f * shown.IndexOf(i);
                _upRow[i].rectTransform.anchoredPosition = new Vector2(34f, y);
                _upName[i].rectTransform.anchoredPosition = new Vector2(52f, y - 7f);
                _upEffect[i].rectTransform.anchoredPosition = new Vector2(52f, y - 33f);
                _upCost[i].rectTransform.anchoredPosition = new Vector2(-224f, y - 15f);
                _upBtn[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(-34f, y - 7f);
                int lvl = v != null ? Loot.SoulLevel(v, i) : 0, cost = Loot.SoulUpgradeCost(i, lvl);
                _upName[i].text = L10n.T("soulUp" + new[] { "Dmg", "Crit", "Coins", "Scales", "Core", "Discount", "TurretDmg", "Knock", "Health" }[i]);
                _upEffect[i].text = L10n.T("soulNow", Loot.SoulNow(i, lvl));
                bool max = lvl >= Loot.SoulMaxLevel(i);
                _upCost[i].text = max ? "Lv" + lvl + " · MAX" : L10n.T("soulUpLevel", lvl, cost);
                _upBtn[i].interactable = host && v != null && souls >= cost && !max;
            }
            for (int i = 0; i < 3; i++)
            {
                _tabBtn[i].GetComponent<Image>().color = i == _soulTab ? Hud.Accent : new Color(0.2f, 0.25f, 0.34f);
                _tabText[i].color = i == _soulTab ? Hud.PanelSolid : Hud.Ink;
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
