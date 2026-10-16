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
    //   markers    - floating labels over the fish crate, the vault, the ready circle and a chest;
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
                ("net", () => Round() && Enemies.Instance.ClientCorpseNear(Me(), 25f)),
                ("netFull", () => Round() && FortressNet.Instance.ClientNetFull),
                ("loot", () => Loot.Instance.ClientHasDrops),
                ("chest", () => Loot.Instance.ClientChestPos != null),
                ("vault", () => FortressIsland.Instance && FortressIsland.Instance.ResultsShowing),
                ("active", () => Round() && Actives.Instance.ClientHasActive),
                ("team", () => Round() && Construction.Instance.ClientOthersBuilding),
            };
            foreach (var k in new[] { EnemyKind.Spitter, EnemyKind.Bomber, EnemyKind.Tank, EnemyKind.Thief, EnemyKind.Shield, EnemyKind.Digger,
                                      EnemyKind.Healer, EnemyKind.Splitter, EnemyKind.Boss, EnemyKind.Queen, EnemyKind.Tide })
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
            if (kb != null && kb[HelpKey].wasPressedThisFrame && !ChatManager.IsTyping && !Hud.ModalOpen) ToggleHelp();
            if (_help && _help.activeSelf && Hud.ModalOpen) _help.SetActive(false);

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
            bool netFull = FortressNet.Instance && FortressNet.Instance.ClientNetFull;
            Marker("crate", ok && round, island ? island.CratePos + Vector3.up * 2f : Vector3.zero, L10n.T("markCrate"), netFull, cam);
            Marker("vault", ok && !round, island ? island.VaultPos + Vector3.up * 3.2f : Vector3.zero, L10n.T("markVault"), false, cam);
            Marker("circle", ok && !round, island ? island.Center + FortressIsland.ReadyLocal + Vector3.up * 2.2f : Vector3.zero, L10n.T("markCircle"), true, cam);
            var chest = Loot.Instance ? Loot.Instance.ClientChestPos : null;
            Marker("chest", ok && chest != null, (chest ?? Vector3.zero) + Vector3.up * 1.6f, L10n.T("markChest"), true, cam);
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

        private GameObject _help;

        public void ToggleHelp()
        {
            if (!_help)
            {
                if (!Hud.Font) return;
                var panel = Hud.Box(Hud.Root, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1320f, 820f), Hud.PanelSolid, 26f);
                _help = panel.gameObject;
                var p = panel.transform;
                Hud.Label(p, L10n.T("helpTitle"), 40f, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(1200f, 50f), TextAlignmentOptions.Center, bold: true);
                Hud.Box(p, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(90f, 5f), Hud.Accent, 2.5f);
                var left = Hud.Label(p, Hud.Keys(L10n.T("helpLeft")), 21f, new Vector2(0f, 1f), new Vector2(48f, -104f), new Vector2(590f, 640f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
                var right = Hud.Label(p, Hud.Keys(L10n.T("helpRight")), 21f, new Vector2(0f, 1f), new Vector2(690f, -104f), new Vector2(590f, 640f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
                left.lineSpacing = right.lineSpacing = 12f;
                Hud.Label(p, Hud.Keys(L10n.T("helpFoot")), 18f, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(1200f, 30f), TextAlignmentOptions.Center, Hud.Muted);
                _help.SetActive(false);
            }
            _help.SetActive(!_help.activeSelf);
            if (_help.activeSelf) { Hud.HideBanner(); AutoTest.Count("help_opened"); }
        }

        public bool HelpOpen => _help && _help.activeSelf;
    }
}
