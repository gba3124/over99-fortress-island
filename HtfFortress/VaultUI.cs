using System;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HtfFortress
{
    // The vault menu: stand at the vault door between rounds and press U. Mouse-driven; the game's own input
    // (moving, looking, using items, the pause menu) is held while it is open.
    public class VaultUI : MonoBehaviour
    {
        public static VaultUI Instance;
        public static bool IsOpen { get; private set; }
        private const float UseRadius = 3.5f;

        private GameObject _root;
        private TextMeshProUGUI _detail, _team;
        private Transform _grid, _fortRow, _mineRow;
        private Button _merge, _equip;
        private TextMeshProUGUI _mergeText, _equipText, _craftText, _salvageText, _scales;
        private Button _craft, _salvage;
        private (Gear g, Rarity r)? _sel;
        private VaultData _drawn;

        private void Awake() => Instance = this;

        private static bool Near()
        {
            var island = FortressIsland.Instance;
            return island != null && island.Exists && Player.LocalPlayer && Player.LocalPlayer.Transform
                   && (Player.LocalPlayer.Transform.position - island.VaultPos).sqrMagnitude < UseRadius * UseRadius;
        }

        private static bool RoundRunning => Siege.Instance && Siege.Instance.ViewActive;

        private void Update()
        {
            bool near = Near();
            if (near && !IsOpen) Hud.Prompt(RoundRunning ? L10n.T("vaultLocked") : L10n.T("vaultPrompt"));
            var kb = Keyboard.current;
            if (IsOpen)
            {
                if (!near || RoundRunning || (kb != null && kb.uKey.wasPressedThisFrame)) Close();
                else if (Loot.Instance.ClientVault != _drawn) Redraw();
                return;
            }
            if (near && !RoundRunning && kb != null && kb.uKey.wasPressedThisFrame && !ChatManager.IsTyping && !BuildSystem.Building && !PauseManager.IsPaused)
                Open();
        }

        public void Open()
        {
            if (!_root) Build();
            if (!_root) return;
            IsOpen = true;
            _root.SetActive(true);
            Hud.Root.gameObject.SetActive(false); // the game's panel sprite isn't fully opaque: keep the HUD from showing through
            PlayerCamera.ToggleMouse(true);
            Redraw();
        }

        // Auto-test: show one item's details.
        public void DebugSelect(Gear g, Rarity r) { _sel = (g, r); if (IsOpen) Redraw(); }

        public void Close()
        {
            IsOpen = false;
            if (_root) _root.SetActive(false);
            Hud.Root.gameObject.SetActive(true);
            PlayerCamera.ToggleMouse(false);
        }

        private static void Send(VaultAction a, Gear g = default, Rarity r = default, int slot = 0)
        {
            if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(new VaultRequest { Action = a, Gear = g, Rarity = r, Slot = (byte)slot });
        }

        // ---------- layout (1920x1080 reference) ----------

        private void Build()
        {
            if (!Hud.Font) return;
            if (!EventSystem.current)
            {
                var es = new GameObject("FortressEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            var canvasGo = new GameObject("FortressVault", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo;

            var dim = Box(canvasGo.transform, new Vector2(4000f, 3000f), Vector2.zero, new Color(0f, 0f, 0f, 0.45f), 0f);
            dim.raycastTarget = true; // clicks outside the panel go nowhere
            var panel = Box(canvasGo.transform, new Vector2(1440f, 860f), Vector2.zero, Hud.PanelSolid, 28f).transform;
            var title = Text(panel, L10n.T("vaultTitle"), 44f, new Vector2(1300f, 60f), new Vector2(0f, 385f), TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            Box(panel, new Vector2(90f, 5f), new Vector2(0f, 360f), Hud.Accent, 2.5f);
            Text(panel, L10n.T("vaultSubtitle"), 21f, new Vector2(1300f, 30f), new Vector2(0f, 334f), TextAlignmentOptions.Center).color = Hud.Muted;

            Text(panel, L10n.T("vaultFortress"), 26f, new Vector2(220f, 70f), new Vector2(-590f, 280f), TextAlignmentOptions.Left);
            _fortRow = new GameObject("FortSlots", typeof(RectTransform)).transform;
            _fortRow.SetParent(panel, false);
            Text(panel, L10n.T("vaultMine"), 26f, new Vector2(220f, 70f), new Vector2(-590f, 195f), TextAlignmentOptions.Left);
            _mineRow = new GameObject("MySlots", typeof(RectTransform)).transform;
            _mineRow.SetParent(panel, false);
            _team = Text(panel, "", 19f, new Vector2(330f, 110f), new Vector2(540f, 205f), TextAlignmentOptions.TopLeft);
            _team.color = new Color(0.75f, 0.8f, 0.85f);

            Box(panel, new Vector2(1380f, 2f), new Vector2(0f, 145f), new Color(1f, 1f, 1f, 0.12f), 0f);
            _grid = new GameObject("Grid", typeof(RectTransform)).transform;
            _grid.SetParent(panel, false);

            Box(panel, new Vector2(500f, 470f), new Vector2(440f, -110f), Hud.Raised, 18f);
            _detail = Text(panel, "", 21f, new Vector2(460f, 330f), new Vector2(440f, -30f), TextAlignmentOptions.TopLeft);
            _scales = Text(panel, "", 24f, new Vector2(300f, 40f), new Vector2(560f, 385f), TextAlignmentOptions.Right);
            _craft = Btn(panel, new Vector2(220f, 56f), new Vector2(320f, -318f), new Color(0.52f, 0.36f, 0.12f), () =>
            {
                if (!(_sel is (Gear g, Rarity r))) return;
                Send(r == Rarity.Legendary ? VaultAction.Enhance : VaultAction.Ascend, g, r);
            }, out _craftText);
            _salvage = Btn(panel, new Vector2(220f, 56f), new Vector2(560f, -318f), new Color(0.36f, 0.3f, 0.3f), () =>
            {
                if (_sel is (Gear g, Rarity r)) Send(VaultAction.Salvage, g, r);
            }, out _salvageText);
            _merge = Btn(panel, new Vector2(220f, 56f), new Vector2(320f, -252f), new Color(0.26f, 0.42f, 0.78f), () =>
            {
                if (_sel is (Gear g, Rarity r)) Send(VaultAction.Merge, g, r);
            }, out _mergeText);
            _equip = Btn(panel, new Vector2(220f, 56f), new Vector2(560f, -252f), new Color(0.22f, 0.58f, 0.38f), () =>
            {
                if (_sel is (Gear g, Rarity r)) Send(GearCatalog.IsFortress(g) ? VaultAction.EquipFortress : VaultAction.EquipPersonal, g, r);
            }, out _equipText);

            Text(panel, Hud.Keys(L10n.T("vaultHelp")), 20f, new Vector2(1300f, 30f), new Vector2(0f, -398f), TextAlignmentOptions.Center).color = Hud.Muted;
            _root.SetActive(false);
        }

        private void Redraw()
        {
            var v = Loot.Instance.ClientVault;
            _drawn = v;
            ulong me = Player.LocalPlayer ? Loot.KeyOf(Player.LocalPlayer) : 0;

            Clear(_fortRow);
            for (int i = 0; i < GearCatalog.FortressSlots; i++)
            {
                int slot = i;
                Slot(_fortRow, i < v.Fortress.Count ? v.Fortress[i] : ((Gear, Rarity)?)null, new Vector2(-370f + 245f * i, 280f), () => Send(VaultAction.UnequipFortress, slot: slot));
            }
            Clear(_mineRow);
            var mine = v.Personal.TryGetValue(me, out var l) ? l : new List<(Gear, Rarity)>();
            // Two passive slots, then the active one (K).
            var passive = mine.Select((x, i) => (x, i)).Where(t => !GearCatalog.IsActive(t.x.Item1)).ToList();
            var active = mine.Select((x, i) => (x, i)).Where(t => GearCatalog.IsActive(t.x.Item1)).ToList();
            for (int i = 0; i < GearCatalog.PersonalSlots; i++)
            {
                bool has = i < passive.Count;
                int slot = has ? passive[i].i : 0;
                Slot(_mineRow, has ? passive[i].x : ((Gear, Rarity)?)null, new Vector2(-370f + 245f * i, 195f), () => Send(VaultAction.UnequipPersonal, slot: slot));
            }
            int aslot = active.Count > 0 ? active[0].i : 0;
            Slot(_mineRow, active.Count > 0 ? active[0].x : ((Gear, Rarity)?)null, new Vector2(120f, 195f), () => Send(VaultAction.UnequipPersonal, slot: aslot), L10n.T("vaultActiveSlot"));
            var others = v.Personal.Where(kv => kv.Key != me && kv.Value.Count > 0)
                .Select(kv => (v.Names.TryGetValue(kv.Key, out var n) ? n : "?") + "：" + string.Join("、", kv.Value.Select(s => GearCatalog.Label(s.Item1, s.Item2)))).ToList();
            _team.text = others.Count > 0 ? L10n.T("vaultTeam") + "\n" + string.Join("\n", others) : "";

            // Collection: fortress gear first, then personal; best rarity first. ponytail: no scrolling, 40 stacks max.
            Clear(_grid);
            var stacks = v.Owned.OrderBy(kv => GearCatalog.IsFortress(kv.Key.Item1) ? 0 : 1).ThenBy(kv => kv.Key.Item1).ThenByDescending(kv => kv.Key.Item2).Take(40).ToList();
            if (stacks.Count == 0) Text(_grid, L10n.T("vaultEmpty"), 26f, new Vector2(800f, 60f), new Vector2(-260f, -100f), TextAlignmentOptions.Center).color = new Color(0.6f, 0.65f, 0.7f);
            for (int i = 0; i < stacks.Count; i++)
            {
                var (g, r) = stacks[i].Key;
                int n = stacks[i].Value;
                Vector2 pos = new Vector2(-610f + 162f * (i % 5), 100f - 66f * (i / 5));
                bool picked = _sel == (g, r);
                var b = Btn(_grid, new Vector2(154f, 58f), pos, picked ? Darken(GearCatalog.Color(r), 0.5f) : new Color(0.13f, 0.15f, 0.19f, 1f), () => { _sel = (g, r); Redraw(); }, out var t);
                Ring(b.transform, new Vector2(154f, 58f), GearCatalog.Color(r) * new Color(1f, 1f, 1f, picked ? 1f : 0.7f));
                t.text = $"{GearCatalog.Name(g)}{(r == Rarity.Legendary && v.EnhanceOf(g) > 0 ? " +" + v.EnhanceOf(g) : "")}\n<size=78%>{GearCatalog.RarityName(r)} ×{n}</size>";
                t.fontSize = 19f;
            }

            // Selected item.
            if (_sel is (Gear sg, Rarity sr) && v.Count(sg, sr) > 0)
            {
                var lines = new List<string> { $"<size=120%>{v.LabelOf(sg, sr)}</size>", L10n.T(GearCatalog.IsFortress(sg) ? "vaultKindFortress" : GearCatalog.IsActive(sg) ? "vaultKindActive" : "vaultKindPersonal"), "" };
                foreach (Rarity rr in Enum.GetValues(typeof(Rarity)))
                    lines.Add((rr == sr ? "▶ " : "　") + $"<color=#{GearCatalog.Hex(rr)}>{GearCatalog.RarityName(rr)}</color>　{GearCatalog.Effect(sg, rr, v.ValueOf(sg, rr))}");
                bool isLegend = sr == Rarity.Legendary;
                if (!GearCatalog.IsActive(sg))
                {
                    string legend = L10n.T("gearLegend" + sg);
                    lines.Add($"<color=#{GearCatalog.Hex(Rarity.Legendary)}>{(isLegend ? "★" : "☆")} {L10n.T("legendTitle")}</color>" + (isLegend ? "" : $"<size=80%><color=#888888>{L10n.T("legendOnly")}</color></size>"));
                    lines.Add($"<size=90%>{(isLegend ? legend : $"<color=#999999>{legend}</color>")}</size>");
                }
                if (v.OwnsLegendary(sg)) lines.Add($"<color=#{GearCatalog.Hex(Rarity.Legendary)}>{L10n.T("vaultEnhanceLevel", v.EnhanceOf(sg), GearCatalog.MaxEnhance)}</color>");
                lines.Add("");
                lines.Add(L10n.T("vaultOwned", v.Count(sg, sr)));
                _detail.text = string.Join("\n", lines);
                bool canMerge = sr != Rarity.Legendary && v.Count(sg, sr) >= 3;
                _merge.interactable = canMerge;
                _mergeText.text = sr == Rarity.Legendary ? L10n.T("vaultMaxed") : L10n.T("vaultMerge", v.Count(sg, sr));
                bool fortress = GearCatalog.IsFortress(sg), act = GearCatalog.IsActive(sg);
                bool full = fortress ? v.Fortress.Count >= GearCatalog.FortressSlots
                          : act ? mine.Count(x => GearCatalog.IsActive(x.Item1)) >= GearCatalog.ActiveSlots
                          : mine.Count(x => !GearCatalog.IsActive(x.Item1)) >= GearCatalog.PersonalSlots;
                bool dup = fortress ? v.Fortress.Any(f => f.Item1 == sg) : mine.Any(m => m.Item1 == sg);
                _equip.interactable = !full && !dup;
                _equipText.text = dup ? L10n.T("vaultAlready") : full ? L10n.T("vaultFull") : L10n.T(fortress ? "vaultEquipFortress" : "vaultEquipMine");
                _merge.gameObject.SetActive(true);
                _equip.gameObject.SetActive(true);
                // Fish scales: rank up (common, rare), enhance (legendary), never epic -> legendary.
                int lvl = v.EnhanceOf(sg);
                if (sr == Rarity.Legendary)
                {
                    bool can = lvl < GearCatalog.MaxEnhance && v.Scales >= GearCatalog.EnhanceCosts[Mathf.Min(lvl, GearCatalog.MaxEnhance - 1)];
                    _craft.interactable = can;
                    _craftText.text = lvl >= GearCatalog.MaxEnhance ? L10n.T("vaultEnhanceMax") : L10n.T("vaultEnhance", lvl + 1, GearCatalog.EnhanceCosts[lvl]);
                }
                else if (sr == Rarity.Epic)
                {
                    _craft.interactable = false;
                    _craftText.text = L10n.T("vaultNoAscend");
                }
                else
                {
                    int cost = GearCatalog.AscendCost(sr);
                    _craft.interactable = v.Scales >= cost;
                    _craftText.text = L10n.T("vaultAscend", cost);
                }
                _salvageText.text = L10n.T("vaultSalvage", GearCatalog.SalvageScales[(int)sr]);
                _craftText.fontSize = _salvageText.fontSize = 19f;
                _craft.gameObject.SetActive(true);
                _salvage.gameObject.SetActive(true);
            }
            else
            {
                _sel = null;
                _detail.text = L10n.T("vaultPick");
                _merge.gameObject.SetActive(false);
                _equip.gameObject.SetActive(false);
                _craft.gameObject.SetActive(false);
                _salvage.gameObject.SetActive(false);
            }
            _scales.text = $"<color=#7fd0ff>◆</color> {L10n.T("vaultScales", v.Scales)}";
        }

        private void Slot(Transform parent, (Gear, Rarity)? item, Vector2 pos, Action unequip, string emptyText = null)
        {
            if (item is (Gear g, Rarity r))
            {
                var b = Btn(parent, new Vector2(230f, 70f), pos, Darken(GearCatalog.Color(r), 0.4f), unequip, out var t);
                Ring(b.transform, new Vector2(230f, 70f), GearCatalog.Color(r));
                var vd = Loot.Instance.ClientVault;
                t.text = $"{GearCatalog.Name(g)}{(r == Rarity.Legendary && vd.EnhanceOf(g) > 0 ? " +" + vd.EnhanceOf(g) : "")}\n<size=75%>{GearCatalog.FormatValue(g, vd.ValueOf(g, r))} · {L10n.T("vaultUnequip")}</size>";
                t.fontSize = 20f;
            }
            else
            {
                var box = Box(parent, new Vector2(230f, 70f), pos, Hud.Raised);
                Text(box.transform, emptyText ?? L10n.T("vaultEmptySlot"), 20f, new Vector2(220f, 60f), Vector2.zero, TextAlignmentOptions.Center).color = new Color(0.5f, 0.55f, 0.6f);
            }
        }

        // ---------- tiny UI kit ----------

        private static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static void Clear(Transform t) { foreach (Transform c in t) Destroy(c.gameObject); }

        private static Image Box(Transform parent, Vector2 size, Vector2 pos, Color c, float radius = 12f)
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.rectTransform.sizeDelta = size;
            img.rectTransform.anchoredPosition = pos;
            Hud.Round(img, radius);
            return img;
        }

        // Rarity ring around a card.
        private static void Ring(Transform card, Vector2 size, Color c)
        {
            var img = Box(card, size, Vector2.zero, c);
            Hud.Round(img, 12f, "Ui_outline");
            img.raycastTarget = false;
        }

        private static TextMeshProUGUI Text(Transform parent, string s, float size, Vector2 box, Vector2 pos, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = Hud.Font;
            t.fontSize = size;
            t.alignment = align;
            t.text = s;
            t.raycastTarget = false;
            t.rectTransform.sizeDelta = box;
            t.rectTransform.anchoredPosition = pos;
            return t;
        }

        private static Button Btn(Transform parent, Vector2 size, Vector2 pos, Color c, Action onClick, out TextMeshProUGUI label)
        {
            var img = Box(parent, size, pos, c);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            label = Text(img.transform, "", 22f, size - new Vector2(10f, 6f), Vector2.zero, TextAlignmentOptions.Center);
            return b;
        }
    }

    // While the vault is open: no moving / looking / using items, the mouse stays free, and Esc closes the vault
    // instead of opening the pause menu.
    [HarmonyPatch(typeof(Player), nameof(Player.BlockInputs), MethodType.Getter)]
    internal static class BlockInputsWhileVaultOpen
    {
        private static void Postfix(ref bool __result) { if (Hud.ModalOpen) __result = true; }
    }

    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ToggleMouse))]
    internal static class FreeMouseWhileVaultOpen
    {
        private static void Prefix(ref bool __0) { if (Hud.ModalOpen) __0 = true; }
    }

    [HarmonyPatch(typeof(PauseManager), "PauseInput")]
    internal static class EscClosesVault
    {
        private static bool Prefix()
        {
            if (!Hud.ModalOpen) return true;
            if (VaultUI.IsOpen) VaultUI.Instance.Close();
            if (PieceMenu.IsOpen) PieceMenu.Instance.Close();
            return false;
        }
    }
}
