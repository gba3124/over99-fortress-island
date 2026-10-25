using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HtfFortress
{
    // Teaching the mode in the game itself:
    //   tip cards  - one short card the first time something happens (first corpse, net full, a thief...), each once per player;
    //   markers    - floating labels over the fish stall, the vault, the ready circle and a chest;
    //   help panel - everything on one page, I to open / close.
    public class Tips : MonoBehaviour
    {
        public static Tips Instance;
        public static ConfigEntry<bool> Show, Reset;
        public const Key HelpKey = Key.I;

        private readonly HashSet<string> _seen = new HashSet<string>();
        private string SeenFile => Path.Combine(Paths.ConfigPath, "owen.htf.fortress.tips.txt");
        private List<(string id, Func<bool> when)> _rules;
        private float _nextCheck;

        private void Awake()
        {
            Instance = this;
            try { if (File.Exists(SeenFile)) foreach (var l in File.ReadAllLines(SeenFile)) _seen.Add(l.Trim()); } catch (Exception) { }
            Vector3 Me() => Player.LocalPlayer && Player.LocalPlayer.Transform ? Player.LocalPlayer.Transform.position : Vector3.zero;
            bool OnIsland() => FortressIsland.Instance && FortressIsland.Instance.Exists && FortressIsland.Instance.OnIsland(Me(), FortressIsland.IslandRadius + 6f);
            bool Round() => Siege.Instance && Siege.Instance.ViewActive;
            _rules = new List<(string, Func<bool>)>
            {
                ("island", () => OnIsland() && !Round()),
                ("start", () => Round() && OnIsland()),
                ("build", () => BuildSystem.Building),

                ("loot", () => Loot.Instance.ClientHasDrops),
                ("chest", () => Loot.Instance.ClientChestPos != null),
                ("scales", () => Loot.Instance.ClientHasScales),
                ("souls", () => Loot.Instance && Loot.Instance.ClientVault != null && Loot.Instance.ClientVault.Souls > 0),
                ("vault", () => FortressIsland.Instance && FortressIsland.Instance.ResultsShowing),
                ("active", () => Round() && Actives.Instance.ClientHasActive),
                ("weapon", () => Round() && Weapons.Instance.ClientHasWeapon),
                ("upkeep", () => Round() && BuildSystem.AimingAtPiece),
                ("team", () => Round() && Construction.Instance.ClientOthersBuilding),
            };
            foreach (var k in new[] { EnemyKind.Spitter, EnemyKind.Bomber, EnemyKind.Tank, EnemyKind.Thief, EnemyKind.Shield, EnemyKind.Digger,
                                      EnemyKind.Healer, EnemyKind.Splitter, EnemyKind.Warden, EnemyKind.Urchin, EnemyKind.Angler, EnemyKind.Steed,
                                      EnemyKind.Stonefish, EnemyKind.Skyfish, EnemyKind.Kraken, EnemyKind.Voxel, EnemyKind.Koi, EnemyKind.Eye })
            {
                var kind = k;
                _rules.Add(("foe" + kind, () => Round() && Enemies.Instance.ClientKindsNear(Me(), 40f).Contains(kind)));
            }
        }

        private void MarkSeen(string id)
        {
            if (!_seen.Add(id)) return;
            try { File.WriteAllLines(SeenFile, _seen); } catch (Exception) { }
        }

        private void Update()
        {
            if (Reset != null && Reset.Value) { Reset.Value = false; _seen.Clear(); try { File.Delete(SeenFile); } catch (Exception) { } }
            var kb = Keyboard.current;
            if (kb != null && kb[HelpKey].wasPressedThisFrame && !ChatManager.IsTyping && (HelpOpen || !Hud.ModalOpen && !PauseManager.IsPaused)) ToggleHelp();
            if (HelpOpen && Hud.MenuOpen) CloseHelp(); // another menu came up over it

            if (Time.time >= _nextCheck && Show != null && Show.Value && Player.LocalPlayer && !Hud.ModalOpen && Time.time > _cardUntil && Hud.Font)
            {
                _nextCheck = Time.time + 0.5f;
                foreach (var (id, when) in _rules)
                {
                    if (_seen.Contains(id)) continue;
                    bool hit;
                    try { hit = when(); } catch (Exception) { hit = false; }
                    if (!hit) continue;
                    ShowCard(id);
                    break;
                }
            }
            AnimateCard();
            DrawMarkers();
        }

        // ---------- tip card (left side) ----------

        private Image _card;
        private TextMeshProUGUI _cardTitle, _cardBody, _cardFoot;
        private CanvasGroup _cardGroup;
        private float _cardStart, _cardUntil;

        public void ShowCard(string id)
        {
            MarkSeen(id);
            if (!_card)
            {
                _card = Hud.Box(Hud.Root, new Vector2(0f, 0.6f), new Vector2(22f, 0f), new Vector2(470f, 160f), Hud.Panel, 16f, pivot: new Vector2(0f, 0.5f));
                _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();
                _cardGroup.blocksRaycasts = false;
                Hud.Box(_card.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(6f, 120f), Hud.Accent, 3f, pivot: new Vector2(0f, 0.5f));
                _cardTitle = Hud.Label(_card.transform, "", 24f, new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(430f, 32f), TextAlignmentOptions.TopLeft, Hud.Accent, bold: true, pivot: new Vector2(0f, 1f));
                _cardBody = Hud.Label(_card.transform, "", 20f, new Vector2(0f, 1f), new Vector2(22f, -48f), new Vector2(430f, 120f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
                _cardFoot = Hud.Label(_card.transform, "", 16f, new Vector2(0f, 0f), new Vector2(22f, 10f), new Vector2(430f, 24f), TextAlignmentOptions.BottomLeft, Hud.Muted, pivot: new Vector2(0f, 0f));
            }
            _cardTitle.text = L10n.T("tip_" + id + "_t");
            string body = Hud.Keys(L10n.T("tip_" + id));
            _cardBody.text = body;
            _cardFoot.text = Hud.Keys(L10n.T("tipFoot"));
            float h = _cardBody.GetPreferredValues(body, 430f, 0f).y;
            _card.rectTransform.sizeDelta = new Vector2(470f, h + 96f);
            _card.transform.GetChild(0).GetComponent<Image>().rectTransform.sizeDelta = new Vector2(6f, h + 70f);
            _cardStart = Time.time;
            _cardUntil = Time.time + Mathf.Clamp(6f + body.Length / 14f, 7f, 14f);
            Fx.Sound("QuestReceived", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.35f, AudioDistance.Short);
            AutoTest.Count("tip_" + id);
        }

        private void AnimateCard()
        {
            if (!_card) return;
            float t = Time.time;
            bool on = t < _cardUntil && !Hud.ModalOpen && !HelpOpen;
            _card.gameObject.SetActive(on);
            if (!on) return;
            float a = Mathf.Clamp01((t - _cardStart) / 0.25f), z = Mathf.Clamp01((_cardUntil - t) / 0.5f);
            _cardGroup.alpha = Mathf.Min(a, z);
            _card.rectTransform.anchoredPosition = new Vector2(22f - 30f * (1f - a), 0f);
        }

        // ---------- floating markers ----------

        private readonly Dictionary<string, (RectTransform root, Image box, TextMeshProUGUI text)> _markers = new Dictionary<string, (RectTransform, Image, TextMeshProUGUI)>();

        private void DrawMarkers()
        {
            var island = FortressIsland.Instance;
            var cam = GameInfo.CurCamera;
            bool ok = island && island.Exists && cam && Hud.Font && Player.LocalPlayer && Player.LocalPlayer.Transform
                      && island.OnIsland(Player.LocalPlayer.Transform.position, FortressIsland.IslandRadius + 40f);
            bool round = Siege.Instance && Siege.Instance.ViewActive;
            bool netFull = false;
            // The stations are always marked (people couldn't find the vault); the vault pulses between rounds, when it can be
            // used, the fish stall when your net is full.
            Marker("crate", ok, island ? island.MarketPos + Vector3.up * 3.3f : Vector3.zero, L10n.T("markCrate"), netFull, cam);
            Marker("vault", ok, island ? island.VaultPos + Vector3.up * 3.2f : Vector3.zero, L10n.T("markVault"), !round, cam);
            Marker("smithy", ok, island ? island.SmithyPos + Vector3.up * 4.6f : Vector3.zero, L10n.T("markSmithy"), false, cam);
            Marker("circle", ok && !round, island ? island.LighthousePos + new Vector3(0f, 2.9f, -1.2f) : Vector3.zero, L10n.T("markCircle"), true, cam); // over the lighthouse's door
            var chest = Loot.Instance ? Loot.Instance.ClientChestPos : null;
            Marker("chest", ok && chest != null, (chest ?? Vector3.zero) + Vector3.up * 1.6f, L10n.T("markChest"), true, cam);
            for (int i = 0; i < 4; i++) // where someone died (their things lie there)
            {
                bool has = i < Fx.DeathMarks.Count;
                var d = has ? Fx.DeathMarks[i] : default;
                Marker("death" + i, ok && has, d.pos + Vector3.up * 1.4f, L10n.T("markDeath", d.who), true, cam);
            }
        }

        private void Marker(string id, bool show, Vector3 world, string text, bool pulse, Camera cam)
        {
            _markers.TryGetValue(id, out var m);
            float dist = show ? Vector3.Distance(cam.transform.position, world) : 0f;
            Vector3 sp = show ? cam.WorldToScreenPoint(world) : Vector3.zero;
            show = show && dist > 5f && dist < 90f && sp.z > 0f && sp.x > 0f && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height;
            if (!show) { if (m.root) m.root.gameObject.SetActive(false); return; }
            if (!m.root)
            {
                var box = Hud.Box(Hud.Root, Vector2.zero, Vector2.zero, new Vector2(200f, 36f), Hud.Panel, 18f, pivot: new Vector2(0.5f, 0f));
                var label = Hud.Label(box.transform, "", 19f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 36f), TextAlignmentOptions.Center, bold: true);
                m = (box.rectTransform, box, label);
                _markers[id] = m;
            }
            m.root.gameObject.SetActive(true);
            m.root.anchorMin = m.root.anchorMax = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
            m.root.anchoredPosition = Vector2.zero;
            string s = $"{text}  <color={Hud.Hex(Hud.Muted)}><size=85%>{Mathf.RoundToInt(dist)}m</size></color>";
            if (m.text.text != s) { m.text.text = s; m.root.sizeDelta = new Vector2(m.text.GetPreferredValues(s).x + 34f, 36f); }
            float k = pulse ? 0.5f + 0.5f * Mathf.Sin(Time.time * 5f) : 0f;
            m.box.color = Color.Lerp(Hud.Panel, new Color(0.55f, 0.36f, 0.08f, 0.85f), k);
            m.root.localScale = Vector3.one * (1f + 0.06f * k) * Mathf.Clamp(22f / Mathf.Max(1f, dist), 0.75f, 1.1f);
        }

        // ---------- help panel (I) ----------
        // A few pictures a page and a few words each (Owen: the old two columns of text were too much to read): how to play,
        // the village, the fish, the weapons. The mouse is free while it's open, like the vault's; I or Esc closes it.

        private GameObject _help;
        private bool _iconsHooked;
        private readonly List<(Image tab, TextMeshProUGUI label, GameObject page)> _tabs = new List<(Image, TextMeshProUGUI, GameObject)>();
        private const float HelpW = 1440f, HelpH = 880f, Top = 136f, Bottom = 58f; // the pages sit between the tabs and the foot

        // (picture, title, line): a picture is Icons/<name>.png (tools/make_icons.py renders the Help ones).
        private static readonly (string pic, string title, string line)[] PlayCards =
        {
            ("HelpPearl", "helpPearlT", "helpPearl"), ("HelpLighthouse", "helpStartT", "helpStart"), ("HelpRevive", "helpReviveT", "helpRevive"),
            ("PieceGun", "helpBuildT", "helpBuild"), ("HelpLoot", "helpLootT", "helpLoot"),
            ("SoulCurrency", "helpSoulT", "helpSoul"),
        };
        private static readonly (string pic, string title, string line)[] VillageCards =
        {
            ("HelpStall", "helpStallT", "helpStall"), ("StallHelper", "helpHelperT", "helpHelper"), ("HelpSmithy", "helpSmithyT", "helpSmithy"),
            ("HelpVault", "helpVaultT", "helpVault"), ("GearWarChest", "helpChestT", "helpChest"), ("HelpLadder", "helpLadderT", "helpLadder"),
        };
        // The special fish, each with its picture HelpFoe<kind> and its line helpFoe<kind>; the bosses come after them.
        private static readonly EnemyKind[] Foes = { EnemyKind.Charger, EnemyKind.Tank, EnemyKind.Spitter, EnemyKind.Bomber, EnemyKind.Thief,
                                                     EnemyKind.Shield, EnemyKind.Digger, EnemyKind.Healer, EnemyKind.Splitter };

        public bool HelpOpen => _help && _help.activeSelf;
        public static bool HelpShown => Instance && Instance.HelpOpen;

        public void ToggleHelp() { if (HelpOpen) CloseHelp(); else OpenHelp(); }

        public void OpenHelp()
        {
            if (!BuildHelp()) return;
            _help.transform.SetAsLastSibling();
            _help.SetActive(true);
            Hud.HideBanner();
            PlayerCamera.ToggleMouse(true);
            AutoTest.Count("help_opened");
        }

        public void CloseHelp()
        {
            if (!HelpOpen) return;
            _help.SetActive(false);
            PlayerCamera.ToggleMouse(false); // stays free if another menu is up (FreeMouseWhileVaultOpen)
        }

        private const int WeaponsTab = 3, SettingsTab = 4;
        public void DebugWeaponsPage() { if (!HelpOpen) OpenHelp(); if (_tabs.Count > WeaponsTab) ShowPage(WeaponsTab); }

        private bool BuildHelp()
        {
            if (_help) return true;
            if (!Hud.Font) return false;
            // The game's guns' pictures come a moment after the page asks for them: a closed page is built again with them.
            if (!_iconsHooked) { _iconsHooked = true; ItemIcons.Ready += () => { if (_help && !_help.activeSelf) { Destroy(_help); _help = null; } }; }
            var panel = Hud.Box(Hud.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(HelpW, HelpH), Hud.PanelSolid, 26f);
            _help = panel.gameObject;
            _help.name = "FortressHelp";
            var p = panel.transform;
            Hud.Label(p, L10n.T("helpTitle"), 36f, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(1200f, 46f), TextAlignmentOptions.Center, bold: true);
            var play = Page(p);
            Cards(play, PlayCards, HelpH - Top - Bottom - 60f);
            Hud.Label(play, Hud.Keys(L10n.T("helpKeys")), 22f, new Vector2(0.5f, 0f), new Vector2(0f, Bottom + 8f), new Vector2(HelpW - 80f, 40f), TextAlignmentOptions.Center, Hud.Muted);
            var village = Page(p);
            Cards(village, VillageCards, HelpH - Top - Bottom);
            var foes = Page(p);
            FoePage(foes);
            var weapons = Page(p);
            WeaponPage(weapons);
            var settings = Page(p);
            SettingsPage(settings);
            var support = Page(p);
            SupportPage(support);
            _tabs.Clear();
            var pages = new[] { play, village, foes, weapons, settings, support };
            var names = new[] { L10n.T("helpTabPlay"), L10n.T("helpTabVillage"), L10n.T("helpTabFoes"), L10n.T("helpTabWeapons"), L10n.T("helpTabSettings"), L10n.T("helpTabSupport") };
            for (int i = 0; i < pages.Length; i++) Tab(p, i, pages.Length, names[i], pages[i].gameObject);
            ShowPage(0);
            Hud.Label(p, Hud.Keys(L10n.T("helpClose")) + "　·　" + VersionCheck.Build, 18f, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1200f, 28f), TextAlignmentOptions.Center, Hud.Muted);
            _help.SetActive(false);
            return true;
        }

        private static RectTransform Page(Transform p)
        {
            var page = new GameObject("Page", typeof(RectTransform)).GetComponent<RectTransform>();
            page.SetParent(p, false); page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one; page.offsetMin = page.offsetMax = Vector2.zero;
            return page;
        }

        // The tabs, a row of pills under the title; the page shown is the orange one.
        private void Tab(Transform p, int i, int n, string text, GameObject page)
        {
            const float w = 210f, gap = 14f;
            var img = Hud.Box(p, new Vector2(0.5f, 1f), new Vector2((i - (n - 1) / 2f) * (w + gap), -74f), new Vector2(w, 46f), Hud.Raised, 23f, pivot: new Vector2(0.5f, 1f));
            img.gameObject.name = "Tab" + i;
            img.raycastTarget = true;
            int index = _tabs.Count;
            img.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowPage(index));
            var label = Hud.Label(img.transform, text, 23f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w - 8f, 42f), TextAlignmentOptions.Center, bold: true);
            _tabs.Add((img, label, page));
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool on = i == index;
                _tabs[i].page.SetActive(on);
                _tabs[i].tab.color = on ? Hud.Accent : Hud.Raised;
                _tabs[i].label.color = on ? Hud.PanelSolid : Hud.Ink;
            }
            if (index == WeaponsTab) AutoTest.Count("help_weapons");
            if (index == SettingsTab) foreach (var show in _settingsShown) show(); // changed meanwhile (the config, the game's own options)
        }

        // Each player's own display settings (ClientSettings.Rows): a line each, a pill that turns it on or off.
        private readonly List<Action> _settingsShown = new List<Action>();
        private void SupportPage(RectTransform page)
        {
            var panel = Hud.Box(page, new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(900f, 620f), Hud.Raised, 22f).transform;
            var T = new Vector2(0.5f, 1f);
            Hud.Label(panel, L10n.T("supportTitle"), 34f, T, new Vector2(0f, -20f), new Vector2(840f, 44f), TextAlignmentOptions.Center, Hud.Accent, bold: true);
            // Keep the copy above the QR: it used to overlap the top of the code at this UI scale.
            var copy = Hud.Label(panel, Hud.Keys(L10n.T("supportText")), 19f, T, new Vector2(0f, -70f), new Vector2(800f, 46f), TextAlignmentOptions.Center, Hud.Ink);
            copy.enableAutoSizing = true; copy.fontSizeMin = 15f; copy.fontSizeMax = 19f;
            Hud.Picture(panel, "DonateQR", T, new Vector2(0f, -126f), new Vector2(240f, 240f), new Vector2(0.5f, 1f));
            Hud.Label(panel, L10n.T("supportScan"), 18f, T, new Vector2(0f, -374f), new Vector2(820f, 24f), TextAlignmentOptions.Center, Hud.Muted);
            Hud.Label(panel, "buymeacoffee.com/gba3124", 19f, T, new Vector2(0f, -402f), new Vector2(820f, 26f), TextAlignmentOptions.Center, Hud.Gold, bold: true);
            var btn = Hud.Box(panel, T, new Vector2(0f, -442f), new Vector2(330f, 46f), new Color(0.24f, 0.48f, 0.32f), 14f);
            btn.raycastTarget = true;
            btn.gameObject.AddComponent<Button>().onClick.AddListener(() => Application.OpenURL("https://buymeacoffee.com/gba3124"));
            Hud.Label(btn.transform, L10n.T("supportOpen"), 20f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 42f), TextAlignmentOptions.Center, Color.white, bold: true);
            Hud.Label(panel, L10n.T("supportContact"), 14f, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(860f, 20f), TextAlignmentOptions.Center, Hud.Muted);
        }

        private void SettingsPage(RectTransform page)
        {
            const float w = 980f, h = 104f, gap = 16f;
            _settingsShown.Clear();
            for (int i = 0; i < ClientSettings.Rows.Count; i++)
            {
                var row = ClientSettings.Rows[i];
                var box = Hud.Box(page, new Vector2(0.5f, 1f), new Vector2(0f, -Top - i * (h + gap)), new Vector2(w, h), Hud.Raised, 18f, pivot: new Vector2(0.5f, 1f)).transform;
                Hud.Label(box, L10n.T(row.Key), 27f, new Vector2(0f, 1f), new Vector2(28f, -14f), new Vector2(w - 240f, 36f), TextAlignmentOptions.TopLeft, bold: true, pivot: new Vector2(0f, 1f));
                var d = Hud.Label(box, L10n.T(row.Key + "D"), 19f, new Vector2(0f, 1f), new Vector2(28f, -54f), new Vector2(w - 240f, 44f), TextAlignmentOptions.TopLeft, Hud.Muted, pivot: new Vector2(0f, 1f));
                d.enableAutoSizing = true; d.fontSizeMin = 14f; d.fontSizeMax = 19f;
                var pill = Hud.Box(box, new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(150f, 52f), Hud.Accent, 26f, pivot: new Vector2(1f, 0.5f));
                pill.raycastTarget = true;
                var text = Hud.Label(pill.transform, "", 24f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(146f, 48f), TextAlignmentOptions.Center, bold: true);
                void Show()
                {
                    bool on = row.On();
                    pill.color = on ? Hud.Accent : Hud.PanelSolid;
                    text.text = L10n.T(on ? "setOn" : "setOff");
                    text.color = on ? Hud.PanelSolid : Hud.Muted;
                }
                Show();
                _settingsShown.Add(Show);
                pill.gameObject.AddComponent<Button>().onClick.AddListener(() => { row.Set(!row.On()); Show(); AutoTest.Count("setting_" + row.Key); });
            }
            Hud.Label(page, L10n.T("setNote"), 20f, new Vector2(0.5f, 0f), new Vector2(0f, Bottom + 8f), new Vector2(HelpW - 80f, 32f), TextAlignmentOptions.Center, Hud.Muted);
        }

        // Two rows of cards across the page: a picture, a title, one line.
        private static void Cards(RectTransform page, (string pic, string title, string line)[] cards, float height)
        {
            const float side = 40f, gap = 20f;
            int cols = (cards.Length + 1) / 2;
            float w = (HelpW - 2f * side - (cols - 1) * gap) / cols, h = (height - gap) / 2f, pic = Mathf.Min(w - 40f, h - 128f);
            for (int i = 0; i < cards.Length; i++)
            {
                var (picture, title, line) = cards[i];
                var card = Hud.Box(page, new Vector2(0f, 1f), new Vector2(side + (i % cols) * (w + gap), -Top - (i / cols) * (h + gap)), new Vector2(w, h), Hud.Raised, 18f, pivot: new Vector2(0f, 1f)).transform;
                Hud.Picture(card, picture, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(pic, pic), new Vector2(0.5f, 1f));
                Hud.Label(card, L10n.T(title), 28f, new Vector2(0.5f, 1f), new Vector2(0f, -pic - 20f), new Vector2(w - 24f, 36f), TextAlignmentOptions.Center, Hud.Accent, bold: true);
                var t = Hud.Label(card, Hud.Keys(L10n.T(line)), 21f, new Vector2(0.5f, 1f), new Vector2(0f, -pic - 62f), new Vector2(w - 36f, h - pic - 68f), TextAlignmentOptions.Top);
                t.enableAutoSizing = true; t.fontSizeMin = 15f; t.fontSizeMax = 21f;
            }
        }

        // The fish in two columns: the kind's picture (its dye, what it wears), its name in its colour, what to do about it.
        private static void FoePage(RectTransform page)
        {
            const float side = 40f, gap = 16f, vgap = 10f;
            int n = Foes.Length + 1, rows = (n + 1) / 2;
            float w = (HelpW - 2f * side - gap) / 2f, h = (HelpH - Top - Bottom - (rows - 1) * vgap) / rows;
            for (int i = 0; i < n; i++)
            {
                bool boss = i == Foes.Length;
                var row = Hud.Box(page, new Vector2(0f, 1f), new Vector2(side + (i % 2) * (w + gap), -Top - (i / 2) * (h + vgap)), new Vector2(w, h), Hud.Raised, 16f, pivot: new Vector2(0f, 1f)).transform;
                Hud.Picture(row, boss ? "HelpBoss" : "HelpFoe" + Foes[i], new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(h - 12f, h - 12f), new Vector2(0f, 0.5f));
                string name = boss ? L10n.T("helpBossT") : L10n.T("enemy" + Foes[i]);
                var color = boss ? Hud.Gold : Color.Lerp(Color.white, Enemies.DyeOf(Foes[i]), 0.8f);
                Hud.Label(row, name, 27f, new Vector2(0f, 0.5f), new Vector2(h + 8f, 22f), new Vector2(w - h - 20f, 36f), TextAlignmentOptions.Left, color, bold: true, pivot: new Vector2(0f, 0.5f));
                var t = Hud.Label(row, L10n.T(boss ? "helpBoss" : "helpFoe" + Foes[i]), 21f, new Vector2(0f, 0.5f), new Vector2(h + 8f, -20f), new Vector2(w - h - 20f, 48f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 0.5f));
                t.enableAutoSizing = true; t.fontSizeMin = 15f; t.fontSizeMax = 21f;
            }
        }

        // Every fortress weapon: its picture, name, what it does as a common one, its legendary number and special.
        private static void WeaponPage(RectTransform page)
        {
            var list = GearCatalog.All.Where(GearCatalog.IsWeapon).ToList();
            // 4 x 5 fits all 20 with room for the 3-line effect; 3 x 7 ran into the footer at this UI scale.
            const int cols = 4;
            int rows = (list.Count + cols - 1) / cols;
            float cw = (HelpW - 80f - (cols - 1) * 15f) / cols, ch = Mathf.Min(130f, (HelpH - Top - Bottom - (rows - 1) * 6f) / rows);
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                var cell = Hud.Box(page, new Vector2(0f, 1f), new Vector2(40f + (i % cols) * (cw + 15f), -Top - (i / cols) * (ch + 6f)), new Vector2(cw, ch), Hud.Raised, 12f, pivot: new Vector2(0f, 1f)).transform;
                Hud.Picture(cell, "Gear" + g, new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(ch - 14f, ch - 14f), new Vector2(0f, 0.5f));
                string text = $"<b>{GearCatalog.Name(g)}</b>\n<size=82%>{GearCatalog.Effect(g, Rarity.Common)}"
                              + $"\n<color=#{GearCatalog.Hex(Rarity.Legendary)}>{L10n.T("helpLegendValue", GearCatalog.FormatValue(g, Rarity.Legendary))}　★ {L10n.T("gearLegend" + g)}</color></size>";
                var t = Hud.Label(cell, text, 15f, new Vector2(0f, 0.5f), new Vector2(ch + 2f, 0f), new Vector2(cw - ch - 10f, ch - 6f), TextAlignmentOptions.Left, pivot: new Vector2(0f, 0.5f));
                t.enableAutoSizing = true; t.fontSizeMin = 9f; t.fontSizeMax = 15f;
            }
        }
    }
}
