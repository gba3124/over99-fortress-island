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
    // Two desks over the one team vault.
    // The quartermaster's (talk to the soldier in the shed, [E]) is a chest: the vault's slots on the left,
    // each up to 50 of one thing (gear, fortress weapons, anything of the game's thrown at the door), your loadout on the
    // right. Drag gear onto a slot of yours to wear it (between rounds), drag it back to take it off; right-click a stack to
    // take one out (it lands at the door).
    // The smith's (the gun store clerk in the smithy) is a workbench over the same vault: pick a weapon or a piece of gear on
    // the left and it goes up on the bench; three of a kind and colour forge into the next colour, scales rank one up,
    // melting one gives scales, a legendary can be enhanced; what's made goes back into the vault. Its last tab is your own
    // weapon mods (WeaponMods).
    // A card on the right says what the thing under the mouse (or picked) does. The game's own input is held meanwhile.
    public class VaultUI : MonoBehaviour
    {
        public static VaultUI Instance;
        public static bool IsOpen { get; private set; }
        public enum Desk { Quartermaster, Smith }
        public Desk CurrentDesk { get; private set; }
        private const float UseRadius = 3.5f;
        private bool Smith => CurrentDesk == Desk.Smith;

        // One vault slot: a (gear, rarity) group (its 0-3★ counts are substacks inside that one slot), or one of the game's
        // items. Stars is the substack shown/selected for the group (default: the highest one there); StarCounts is that
        // group's 0★/1★/2★/3★ breakdown (null when this Pile was made by hand, e.g. a shop tile).
        private struct Pile
        {
            public Gear? Gear; public Rarity Rarity; public byte Item; public int Count;
            public int Stars; public int[] StarCounts;
            public bool IsItem => !Gear.HasValue;
            public int StarCount(int s) => StarCounts != null && s >= 0 && s < StarCounts.Length ? StarCounts[s] : s == Stars ? Count : 0;
            public bool Same(Pile o) => IsItem ? o.IsItem && o.Item == Item : !o.IsItem && o.Gear == Gear && o.Rarity == Rarity;
        }

        // What's on screen: a slot of the loadout (kind, place in its row, index in the vault's list). Item is the equipped
        // gear key: the stars too, so taking it off / dragging it puts the same substack back.
        private struct SlotRef { public int Kind, Visual, List; public GearKey? Item; }

        private GameObject _root;
        private Transform _gridLayer, _tabsLayer, _rightLayer, _cardLayer, _dragLayer;
        private TextMeshProUGUI _title, _subtitle, _scales, _hint, _capacity, _expandText;
        private UnityEngine.UI.Image _scaleIcon;
        private Button _expandBtn;
        private VaultData _drawn;
        private int _lastSig; private bool _sigKnown; private float _nextSig, _nextRedraw;
        private bool _syncPending; private float _syncAt;
        private float _lastRedrawMs;
        private int _tab;                 // vault: 0 all, 1 gear, 2 weapons, 3 the game's items; smithy: 0 weapons, 1 gear, 2 the shop, 3 mods
        private int _gearSide;            // the gear tab's filter: 0 all, 1 for people (personal + active), 2 for towers (fortress)
        private const int ShopTab = 2, ModTab = 3;
        private bool Shop => Smith && _tab == ShopTab;
        private bool Modding => Smith && _tab == ModTab;
        private Pile? _sel, _hover;
        // Which substack a gear group's tile shows/equips (0-3★): the bench's star buttons (or the highest star there when
        // nothing was picked). Only remembered while it still has pieces in it; the highest star otherwise.
        private readonly Dictionary<(Gear, Rarity), int> _pickedStar = new Dictionary<(Gear, Rarity), int>();
        private SlotRef? _hoverSlot;
        private Expansion? _hoverBuy;
        private string _benchHover;       // the bench button under the mouse: "merge", "ascend", "salvage", "enhance"
        private Image _ghost;             // what's being dragged
        private Canvas _canvas;
        private Transform _hotbarLayer;   // a handle over each of the game's hotbar slots: drag onto it to take out into that slot, from it to store
        private readonly List<(Image handle, RectTransform slot)> _hotbar = new List<(Image, RectTransform)>();
        private int _dragHotbar = -1;     // the hotbar slot being dragged from
        private GameObject _zone;         // "let go here to take it out", while a weapon or an item is dragged
        private bool _iconsPending;

        private void Awake()
        {
            Instance = this;
            ItemIcons.Ready += () => { if (IsOpen) _iconsPending = true; };
        }

        private static bool Near(Desk d)
        {
            var island = FortressIsland.Instance;
            return island != null && island.Exists && Player.LocalPlayer && Player.LocalPlayer.Transform
                   && (Player.LocalPlayer.Transform.position - (d == Desk.Smith ? island.SmithyPos : island.VaultPos)).sqrMagnitude < UseRadius * UseRadius;
        }


        // Opened by talking to the keeper inside ([E], Keepers.cs: Owen didn't need U at the door as well); Esc closes.
        private void Update()
        {
            if (IsOpen)
            {
                if (!Near(CurrentDesk)) Close();
                else if (_dragPile == null)
                {
                    // The vault is re-sent whenever anyone puts something in (twice a second while helpers are working); rebuilding
                    // the whole panel each time made opening it feel like a freeze. Cheap checks: a signature at 5 Hz, and a
                    // redraw at most twice a second, timed so a slow one is in the log.
                    bool expired = _sellArmed >= 0 && Time.unscaledTime >= _sellArmedUntil;
                    if (Time.unscaledTime >= _nextSig) { _nextSig = Time.unscaledTime + 0.2f; RecheckSignature(); }
                    if ((_iconsPending || expired || !_sigKnown || _lastSig != _sigSeen) && Time.unscaledTime >= _nextRedraw)
                    {
                        _iconsPending = false;
                        if (expired) _sellArmed = -1;
                        _sigKnown = true; _lastSig = _sigSeen;
                        float t0 = Time.realtimeSinceStartup;
                        if (Modding && _sigKnown && !_iconsPending) RedrawModsOnly(); else Redraw();
                        _lastRedrawMs = (Time.realtimeSinceStartup - t0) * 1000f;
                        if (_lastRedrawMs > 40f) BuildSystem.Log.LogWarning($"vault redraw {_lastRedrawMs:0} ms");
                        _nextRedraw = Time.unscaledTime + 0.5f;
                    }
                }
                if (_syncPending && Time.unscaledTime >= _syncAt)
                {
                    _syncPending = false;
                    Loot.Instance.RequestVaultSync();
                }
                if (_ghost) _ghost.rectTransform.position = Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : _ghost.rectTransform.position;
                PlaceHotbarHandles();
            }
        }

        public void Open(Desk desk = Desk.Quartermaster)
        {
            bool firstBuild = !_root;
            float tb = Time.realtimeSinceStartup;
            if (!_root) Build();
            if (!_root) return;
            if (firstBuild)
            {
                float bms = (Time.realtimeSinceStartup - tb) * 1000f;
                BuildSystem.Log.LogInfo($"vault panel built once: {bms:0} ms");
            }
            bool anotherDesk = CurrentDesk != desk;
            if (anotherDesk) { _sel = null; _tab = 0; _gearSide = 0; }
            CurrentDesk = desk;
            _title.text = L10n.T(Smith ? "smithTitle" : "vaultTitle");
            _subtitle.text = L10n.T(Smith ? "smithSubtitle" : "vaultSubtitle");
            _hint.text = Hud.Keys(L10n.T(Smith ? "smithHint" : "vaultHint"));
            AutoTest.Count(Smith ? "smith_opened" : "vault_opened");
            IsOpen = true;
            _root.SetActive(true);
            Hud.Root.gameObject.SetActive(false); // the game's panel sprite isn't fully opaque: keep the HUD from showing through
            PlayerCamera.ToggleMouse(true);
            _hover = null; _hoverSlot = null; _hoverBuy = null; _benchHover = null; _hoverSell = null; _sellArmed = -1;
            // Opening it again with the same things in it reuses the panel built last time (the old code rebuilt every open).
            if (anotherDesk || !_sigKnown || Signature(Loot.Instance.ClientVault) != _lastSig)
            {
                float t0 = Time.realtimeSinceStartup;
                Redraw();
                float ms = (Time.realtimeSinceStartup - t0) * 1000f;
                if (ms > 40f) BuildSystem.Log.LogWarning($"vault open {ms:0} ms (rebuilt)");
            }
            _lastSig = Signature(Loot.Instance.ClientVault); _sigSeen = _lastSig; _sigKnown = true; _nextSig = Time.unscaledTime + 0.2f; _nextRedraw = Time.unscaledTime + 0.5f;
            BuildHotbarHandles();
        }

        // ---------- the game's hotbar (1-n) as a drag target and source while the vault is open ----------

        private void BuildHotbarHandles()
        {
            foreach (Transform c in _hotbarLayer) Destroy(c.gameObject);
            _hotbar.Clear();
            var me = Player.LocalPlayer;
            var slots = me && me.Inventory ? HarmonyLib.AccessTools.Field(typeof(PlayerInventory), "_itemSlots").GetValue(me.Inventory) as List<InventorySlot> : null;
            if (slots == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i] || !slots[i].gameObject.activeInHierarchy || !(slots[i].transform is RectTransform rt)) continue;
                int index = i;
                var go = new GameObject("HotbarHandle" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_hotbarLayer, false);
                var img = go.GetComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.004f); // there to catch the mouse, not to be seen
                img.raycastTarget = true;
                go.AddComponent<VaultDrop>().Dropped = () => DroppedOnHotbar(index);
                var t = go.AddComponent<VaultTile>();
                t.BeginDrag = () => BeginHotbarDrag(index);
                t.EndDrag = EndDrag;
                _hotbar.Add((img, rt));
            }
            PlaceHotbarHandles();
        }

        // Over the slots wherever the game draws them (its canvas may be the screen's or a camera's).
        private void PlaceHotbarHandles()
        {
            if (!_canvas) return;
            var corners = new Vector3[4];
            foreach (var (handle, slot) in _hotbar)
            {
                if (!handle || !slot) continue;
                var gc = slot.GetComponentInParent<Canvas>();
                slot.GetWorldCorners(corners);
                Vector2 a = ToScreen(corners[0], gc), b = ToScreen(corners[2], gc);
                var rt = handle.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.position = (a + b) / 2f;
                rt.sizeDelta = (b - a) / Mathf.Max(0.01f, _canvas.scaleFactor);
            }
        }

        private static Vector2 ToScreen(Vector3 world, Canvas c) =>
            !c || c.renderMode == RenderMode.ScreenSpaceOverlay ? (Vector2)world : RectTransformUtility.WorldToScreenPoint(c.worldCamera, world);

        // A weapon or one of the game's items from the vault let go over hotbar slot `i`: one comes out into that slot.
        private void DroppedOnHotbar(int i)
        {
            if (_dragHotbar >= 0 || _dragSlot != null || !(_dragPile is Pile p)) return;
            if (p.IsItem) Send(VaultAction.TakeItem, slot: Smith ? 1 : 0, item: p.Item, count: 1, into: i + 1);
            else if (GearCatalog.IsWeapon(p.Gear.Value) && GroupCount(Loot.Instance.ClientVault, p.Gear.Value, p.Rarity) > 0) Send(VaultAction.Take, p.Gear.Value, p.Rarity, slot: Smith ? 1 : 0, count: 1, into: i + 1, stars: p.Stars);
            else return;
            Click("UnequipItem");
            AutoTest.Count("hotbar_drop");
        }

        // Picked up from hotbar slot `i`: what's in it, to let go over the vault (it goes in).
        private void BeginHotbarDrag(int i)
        {
            var me = Player.LocalPlayer;
            if (!me || !me.Inventory || !me.Inventory._items.TryGetValue((byte)i, out var item) || !item) return;
            var pile = WeaponItems.AsFortress(item, out var g, out var r) ? new Pile { Gear = g, Rarity = r, Stars = WeaponItems.StarsOf(item), Count = 1 } : new Pile { Item = item.ID, Count = 1 };
            BeginDrag(pile, null, null, i);
        }

        // Auto-test: a weapon from the vault dragged onto hotbar slot i; what's in slot i dragged onto the vault; the slots lit
        // as during a drag (for a picture).
        public void DebugHotbarTake(Gear g, Rarity r, int i, int stars = 0) { _dragPile = new Pile { Gear = g, Rarity = r, Stars = stars, Count = 1 }; DroppedOnHotbar(i); EndDrag(); }
        public void DebugHotbarStore(int i) { BeginHotbarDrag(i); DroppedOnVault(); EndDrag(); }
        public void DebugHotbarLight(bool on) { if (on) BuildHotbarHandles(); LightHotbar(on); }
        public int DebugHotbarHandles => _hotbar.Count;
        public List<int> DebugHotbarSlots => _hotbar.Where(h => h.handle).Select(h => int.Parse(h.handle.name.Substring("HotbarHandle".Length))).ToList();

        private void LightHotbar(bool on)
        {
            foreach (var (handle, _) in _hotbar) if (handle) handle.color = on ? new Color(0.3f, 0.95f, 0.5f, 0.28f) : new Color(1f, 1f, 1f, 0.004f);
        }

        // Auto-test: a tab.
        public void DebugTab(int tab) { _tab = tab; if (IsOpen) Redraw(); }

        // Auto-test: show one thing's card (and put it on the bench at the smith's). `stars` -1 = the highest one there.
        public void DebugSelect(Gear g, Rarity r, int stars = -1) { _sel = Selection(Loot.Instance.ClientVault, g, r, stars); if (IsOpen) Redraw(); }
        // Auto-test: which substack the bench works on (the four star buttons); -1 = the highest one there.
        public void DebugBenchStar(int stars)
        {
            if (!(_sel is Pile p) || p.IsItem) return;
            p.Stars = stars < 0 ? HighestStar(StarCountsOf(Loot.Instance.ClientVault, p.Gear.Value, p.Rarity)) : Mathf.Clamp(stars, 0, GearCatalog.MaxStars);
            _sel = p;
            _pickedStar[(p.Gear.Value, p.Rarity)] = p.Stars;
            if (IsOpen) Redraw();
        }
        // Auto-test: the star glyphs the UI shows for a group's tile (and how many of each substack the card would list).
        public string DebugStars(Gear g, Rarity r)
        {
            var v = Loot.Instance.ClientVault;
            var counts = StarCountsOf(v, g, r);
            return StarMark(GroupPile(v, g, r).Stars) + " " + string.Join(" ", counts.Select((n, s) => $"{s}*{n}"));
        }
        public Button[] DebugButtons() => _rightLayer.GetComponentsInChildren<Button>();

        public void Close()
        {
            IsOpen = false;
            EndDrag();
            if (_root) _root.SetActive(false);
            Hud.Root.gameObject.SetActive(true);
            PlayerCamera.ToggleMouse(false);
        }

        private int _sigSeen;
        private void RecheckSignature() => _sigSeen = Signature(Loot.Instance.ClientVault);

        // A cheap number for "does the panel show something different" (no strings, no LINQ): the redraw is expensive.
        private static int Signature(VaultData v)
        {
            if (v == null) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + v.Scales; h = h * 31 + v.Best;
                h = h * 31 + v.SlotsOf(0); h = h * 31 + v.SlotsOf(1); h = h * 31 + v.StorageSlots;
                h = h * 31 + v.Fortress.Count + v.Items.Count;
                foreach (var kv in v.Owned) { h = h * 31 + (int)kv.Key.Item1 + (int)kv.Key.Item2 * 7 + kv.Key.Stars * 131 + kv.Value * 7919; }
                foreach (var kv in v.Personal) h = h * 31 + kv.Value.Count;
                foreach (var kv in v.ItemCount) { h = h * 31 + kv.Key; h = h * 31 + kv.Value; }
                foreach (var kv in v.Bought) { h = h * 31 + (int)kv.Key; h = h * 31 + kv.Value; }
                foreach (var kv in v.Enhance) { h = h * 31 + (int)kv.Key; h = h * 31 + kv.Value; }
                h = h * 31 + (v.Oven ? 1 : 0);
                return h;
            }
        }

        // A double click (or a click repeated before the host's answer) used to send the same request twice: the host
        // correctly applied one, but the panel redrew with stale levels in between, which looked broken.
        private static VaultAction _lastAction; private static Gear _lastGear; private static Rarity _lastRarity; private static int _lastSlot, _lastItem, _lastAt, _lastStars;
        private static float _modBusyUntil; private static int _modBusyKey;

        private static void Send(VaultAction a, Gear g = default, Rarity r = default, int slot = 0, byte item = 0, int count = 1, int into = 0, int stars = 0)
        {
            stars = Mathf.Clamp(stars, 0, GearCatalog.MaxStars);
            int now = Time.frameCount;
            if (a == _lastAction && g == _lastGear && r == _lastRarity && slot == _lastSlot && item == _lastItem && stars == _lastStars && now - _lastAt <= 1) return;
            _lastAction = a; _lastGear = g; _lastRarity = r; _lastSlot = slot; _lastItem = item; _lastStars = stars; _lastAt = now;
            if (a == VaultAction.Mod) { _modBusyKey = (slot << 16) | (int)g; _modBusyUntil = Time.unscaledTime + 0.12f; }
            else if (a != VaultAction.Sync && a != VaultAction.SellAll && a != VaultAction.Buy && a != VaultAction.BuyItem && a != VaultAction.Take && a != VaultAction.TakeItem && a != VaultAction.Deposit && a != VaultAction.StoreSlot)
            {
                // Preview the same action locally: the labels/rows move on the click, then the host's vault corrects any
                // difference (a race, a full vault, someone else taking the last one).
                var cv = Loot.Instance ? Loot.Instance.ClientVault : null;
                if (cv != null && cv.Apply(a, g, r, slot, Loot.MyKey, stars))
                {
                    // Ask the host for the real vault a moment later: if it rejected the action (a race, a full vault), the
                    // optimistic preview must not stay on screen.
                    if (Instance) { Instance._syncPending = true; Instance._syncAt = Time.unscaledTime + 0.8f; }
                }
            }
            if (InstanceFinder.IsClientStarted)
                InstanceFinder.ClientManager.Broadcast(new VaultRequest { Action = a, Gear = g, Rarity = r, Slot = (byte)slot, Item = item, Count = (byte)Mathf.Clamp(count, 0, 255), Into = (byte)into, Stars = (byte)stars });
            if (a != VaultAction.Mod) RefreshSoon(); // mods wait for the host's answer: instantly rebuilding with the old level looked wrong
        }

        // Something was clicked: let the next frame redraw (the rate limits still bound how often that can happen).
        public static void RefreshSoon()
        {
            if (!Instance) return;
            Instance._sigKnown = false; Instance._nextSig = 0f; Instance._nextRedraw = 0f;
        }

        private static void Click(string sound = "EquipItem") => Fx.Sound(sound, Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.6f, AudioDistance.Short);

        // ---------- layout (1920x1080 reference; the panel's middle is 0,0) ----------

        private static readonly Vector2 GridTopLeft = new Vector2(-790f, 280f);
        private const float GridWidth = 900f, GridHeight = 560f, RightX = 470f, RightWidth = 640f;
        private const int Columns = 10;
        // The mods tab's tiles: bronze, no rarity's colour (a mod is on the kind, whatever its rarity).
        private static readonly Color ModTint = new Color(0.22f, 0.17f, 0.09f, 1f), ModRing = new Color(0.95f, 0.75f, 0.3f, 0.6f);

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
            _canvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo;

            var dim = Box(canvasGo.transform, new Vector2(4000f, 3000f), Vector2.zero, new Color(0f, 0f, 0f, 0.45f), 0f);
            dim.raycastTarget = true; // clicks outside the panel go nowhere
            var panel = Box(canvasGo.transform, new Vector2(1640f, 940f), Vector2.zero, Hud.PanelSolid, 28f).transform;
            _title = Text(panel, "", 42f, new Vector2(1300f, 56f), new Vector2(0f, 425f), TextAlignmentOptions.Center);
            _title.fontStyle = FontStyles.Bold;
            Box(panel, new Vector2(90f, 5f), new Vector2(0f, 398f), Hud.Accent, 2.5f);
            _subtitle = Text(panel, "", 20f, new Vector2(1400f, 28f), new Vector2(0f, 376f), TextAlignmentOptions.Center);
            _subtitle.color = Hud.Muted;
            _scales = Text(panel, "", 24f, new Vector2(300f, 40f), new Vector2(640f, 425f), TextAlignmentOptions.Right);
            _scaleIcon = Hud.Picture(panel, "ScaleCurrency", new Vector2(1f, 1f), new Vector2(-250f, 425f), new Vector2(28f, 28f), pivot: new Vector2(0f, 1f));

            // Left: the vault's slots (a drop target too: drop equipped gear here to take it off).
            var well = Box(panel, new Vector2(GridWidth + 30f, GridHeight + 130f), GridTopLeft + new Vector2(GridWidth / 2f, -GridHeight / 2f + 20f), Hud.Raised, 18f);
            well.raycastTarget = true;
            well.gameObject.AddComponent<VaultDrop>().Dropped = DroppedOnVault;
            _tabsLayer = Node(panel, "Tabs");
            _gridLayer = Node(panel, "Grid");
            _capacity = Text(panel, "", 19f, new Vector2(420f, 30f), GridTopLeft + new Vector2(210f, -GridHeight - 22f), TextAlignmentOptions.Left);
            _expandBtn = Btn(panel, new Vector2(300f, 34f), GridTopLeft + new Vector2(GridWidth - 150f, -GridHeight - 22f), new Color(0.36f, 0.27f, 0.1f), () => { if (CanBuy(Expansion.Storage)) Buy(Expansion.Storage); }, out _expandText);
            _expandText.fontSize = 16f;
            Hover(_expandBtn.gameObject, () => { _hoverBuy = Expansion.Storage; RedrawCard(); }, () => { _hoverBuy = null; RedrawCard(); });

            // Right: the loadout (or the bench), then the card.
            _rightLayer = Node(panel, "Right");
            _cardLayer = Node(panel, "Card");
            _hint = Text(panel, "", 18f, new Vector2(1560f, 28f), new Vector2(0f, -452f), TextAlignmentOptions.Center);
            _hint.color = Hud.Muted;
            _hotbarLayer = Node(canvasGo.transform, "Hotbar");
            _dragLayer = Node(canvasGo.transform, "Drag");
            _root.SetActive(false);
        }

        // ---------- what's in the vault, as slots ----------

        private static int KindOf(Gear g) => GearCatalog.SlotKind(g);

        // ---------- stars (0 = none, 1-3 = forged) ----------

        // A tile's compact badge: the star it shows, or a dash for none.
        private static string StarMark(int stars) => stars <= 0 ? "—" : new string('★', Mathf.Clamp(stars, 0, GearCatalog.MaxStars));
        // Stars in a sentence (merge chains, the card's per-star counts): 0★ rather than the tile's dash.
        private static string StarName(int stars) => stars <= 0 ? "0★" : new string('★', Mathf.Clamp(stars, 0, GearCatalog.MaxStars));

        // The 0★/1★/2★/3★ counts inside one (gear, rarity) slot. The vault keeps one slot per group, the stars as substacks.
        private static int[] StarCountsOf(VaultData v, Gear g, Rarity r)
        {
            var a = new int[GearCatalog.MaxStars + 1];
            for (int s = 0; s < a.Length; s++) a[s] = v.Count(g, r, s);
            return a;
        }
        private static int HighestStar(int[] counts)
        {
            for (int s = counts.Length - 1; s >= 0; s--) if (counts[s] > 0) return s;
            return 0;
        }
        // Everything of that gear and rarity, every star together: what the slot holds and what a sale/take counts.
        private static int GroupCount(VaultData v, Gear g, Rarity r) { int n = 0; for (int s = 0; s <= GearCatalog.MaxStars; s++) n += v.Count(g, r, s); return n; }
        // The substack to use: `want` if it has pieces, else the highest one there. Never asks for an empty star.
        private static int AvailableStar(VaultData v, Gear g, Rarity r, int want)
        {
            want = Mathf.Clamp(want, 0, GearCatalog.MaxStars);
            if (v.Count(g, r, want) > 0) return want;
            for (int s = GearCatalog.MaxStars; s >= 0; s--) if (v.Count(g, r, s) > 0) return s;
            return want;
        }
        // A Pile for one group: the star shown is the one last picked for it at the bench, else the highest one there.
        private Pile GroupPile(VaultData v, Gear g, Rarity r)
        {
            var counts = StarCountsOf(v, g, r);
            int total = 0;
            for (int s = 0; s < counts.Length; s++) total += counts[s];
            int stars = _pickedStar.TryGetValue((g, r), out int picked) && picked >= 0 && picked <= GearCatalog.MaxStars && counts[picked] > 0 ? picked : HighestStar(counts);
            return new Pile { Gear = g, Rarity = r, Count = total, Stars = stars, StarCounts = counts };
        }
        // A hand-made selection (a bench pick, a card): `stars` -1 means the highest substack the vault has.
        private static Pile Selection(VaultData v, Gear g, Rarity r, int stars = -1)
        {
            var counts = StarCountsOf(v, g, r);
            int total = 0;
            for (int s = 0; s < counts.Length; s++) total += counts[s];
            int star = stars < 0 ? HighestStar(counts) : Mathf.Clamp(stars, 0, GearCatalog.MaxStars);
            if (stars >= 0 && counts[star] == 0 && total > 0) star = HighestStar(counts);
            return new Pile { Gear = g, Rarity = r, Count = Mathf.Max(1, total), Stars = star, StarCounts = counts };
        }
        // "3 × Rare ★★ → Rare ★★★", or "... → Legendary 0★" from 3★, or "highest" at Legendary 3★.
        private static string MergeChain(Rarity r, int stars)
        {
            if (stars < GearCatalog.MaxStars) return $"3 × {GearCatalog.RarityName(r)} {StarName(stars)} → {GearCatalog.RarityName(r)} {StarName(stars + 1)}";
            if (r < Rarity.Legendary) return $"3 × {GearCatalog.RarityName(r)} {StarName(stars)} → {GearCatalog.RarityName(r + 1)} 0★";
            return L10n.T("vaultMaxed");
        }
        // The star row put in a tile's bottom-left corner (gear and weapons; the game's items have none).
        private static void StarBadge(Transform tile, float size, int stars)
        {
            var t = Text(tile, StarMark(stars), Mathf.Clamp(size * 0.17f, 9f, 15f), new Vector2(size * 0.75f, 18f),
                         new Vector2(-size / 2f + 3f + size * 0.375f, -size / 2f + 10f), TextAlignmentOptions.BottomLeft);
            t.color = stars > 0 ? Hud.Gold : new Color(1f, 1f, 1f, 0.32f);
            t.fontStyle = FontStyles.Bold;
            t.outlineWidth = 0.18f; t.outlineColor = new Color(0f, 0f, 0f, 0.85f);
        }
        // The star suffix in a name (" [Legendary] Laser ★★").
        private static string StarSuffix(int stars) => stars > 0 ? $" <color=#{Hud.Hex(Hud.Gold)}>{new string('★', Mathf.Clamp(stars, 0, GearCatalog.MaxStars))}</color>" : "";

        // What the piece does with the selected star (VaultData.EffectFor has no star yet: same maths, from its public parts).
        private static string EffectOf(VaultData v, Gear g, Rarity r, int stars) =>
            !GearCatalog.IsWeapon(g) ? GearCatalog.Effect(g, r, v.ValueOf(g, r, stars))
            : GearCatalog.Effect(g, r, v.WeaponValue(Loot.MyKey, g, r, stars), WeaponMods.TimeMult(v.ModOf(Loot.MyKey, g, WeaponMods.Rate)));

        // Weapons first, then gear (team, personal, active), then the game's items; best rarity first. ONE tile per
        // (gear, rarity) slot (its 0-3★ substacks share the vault slot), the game's items still 50 to a slot.
        private List<Pile> Piles(VaultData v)
        {
            var list = new List<Pile>();
            foreach (var g in GearCatalog.All.Where(g => !GearCatalog.Retired(g)).OrderBy(g => GearCatalog.IsWeapon(g) ? 0 : 1).ThenBy(g => g))
                foreach (var r in GearCatalog.Rarities.OrderByDescending(r => r))
                {
                    var pile = GroupPile(v, g, r);
                    if (pile.Count > 0) list.Add(pile);
                }
            foreach (var kv in v.ItemCount.OrderBy(kv => kv.Key))
                for (int n = kv.Value; n > 0; n -= GearCatalog.Stack) list.Add(new Pile { Item = kv.Key, Count = Mathf.Min(n, GearCatalog.Stack) });
            return list;
        }

        private bool InTab(Pile p) => InTab(p, _tab);
        private bool GearSide(Gear g) => _gearSide == 0 || (_gearSide == 1 ? (KindOf(g) == 1 || KindOf(g) == 2) : KindOf(g) == 0);
        private bool InTab(Pile p, int tab)
        {
            if (Smith) return !p.IsItem && (tab == 0 ? GearCatalog.IsWeapon(p.Gear.Value) : !GearCatalog.IsWeapon(p.Gear.Value));
            switch (tab)
            {
                // Gear for the people (personal and active) or for the towers (fortress/team buffs): Owen wanted them apart.
                case 1: return !p.IsItem && !GearCatalog.IsWeapon(p.Gear.Value) && GearSide(p.Gear.Value);
                case 2: return !p.IsItem && GearCatalog.IsWeapon(p.Gear.Value);
                case 3: return p.IsItem;
                default: return true;
            }
        }

        private static int Total(VaultData v, Pile p) => p.IsItem ? v.CountItem(p.Item) : GroupCount(v, p.Gear.Value, p.Rarity);

        private static string NameOf(Pile p)
        {
            if (!p.IsItem) return GearCatalog.Name(p.Gear.Value);
            var prefab = GameInfo.GetSpawnable(p.Item);
            string n = "";
            try { n = prefab ? prefab.GetName() : ""; } catch { }
            return string.IsNullOrEmpty(n) ? (prefab ? prefab.name : "#" + p.Item) : n;
        }

        // ---------- drawing ----------

        private void Redraw()
        {
            // The buttons just destroyed are rebuilt below: forget what the pointer was over, or the card shows the old hover.
            _hover = null; _hoverSlot = null; _hoverBuy = null; _benchHover = null; _hoverSell = null;
            var v = Loot.Instance.ClientVault;
            _drawn = v;
            if (_sel is Pile s && Total(v, s) == 0 && !Shop && !Modding && !(Smith && !s.IsItem && Carried().Any(x => x.Item1 == s.Gear.Value && x.Item2 == s.Rarity))) _sel = null;
            _scales.text = L10n.T("vaultScales", v.Scales);
            DrawTabs(v);
            DrawGrid(v);
            Clear(_rightLayer);
            if (Shop) DrawShop(); else if (Modding) DrawMods(v); else if (Smith) DrawBench(v); else DrawLoadout(v);
            DrawCapacity(v);
            if (!Smith) DrawSale(v);
            RedrawCard();
        }

        // The host answers a mod buy immediately, but the glass on the client is a round trip away. Show the next level at
        // once (it is replaced by the host's vault a moment later).
        private static void PreviewMod(Gear g, int track, int lvl)
        {
            var v = Loot.Instance ? Loot.Instance.ClientVault : null;
            if (v == null) return;
            int cost = WeaponMods.Cost(lvl);
            v.Scales = Mathf.Max(0, v.Scales - cost);
            var key = (Loot.MyKey, g, track);
            v.Mods[key] = lvl + 1;
            if (Instance) { Instance._syncPending = true; Instance._syncAt = Time.unscaledTime + 0.8f; }
            if (Instance && Instance._scales) Instance._scales.text = L10n.T("vaultScales", v.Scales);
        }

        // Buying a mod only changes the right-hand rows: keep the grid/tabs and update just that column.
        private void RedrawModsOnly()
        {
            var v = Loot.Instance.ClientVault;
            _drawn = v;
            _scales.text = L10n.T("vaultScales", v.Scales);
            Clear(_rightLayer);
            DrawMods(v);
            DrawCapacity(v);
            RedrawCard();
        }

        private void DrawTabs(VaultData v)
        {
            Clear(_tabsLayer);
            var piles = Piles(v);
            string[] keys = Smith ? new[] { "vaultTabWeapons", "vaultTabGear", "smithTabShop", "smithTabMods" } : new[] { "vaultTabAll", "vaultTabGear", "vaultTabWeapons", "vaultTabItems" };
            for (int k = 0; k < keys.Length; k++)
            {
                int tab = k;
                int count = piles.Where(p => InTab(p, tab)).Sum(p => p.Count);
                var b = Btn(_tabsLayer, new Vector2(150f, 36f), GridTopLeft + new Vector2(85f + 160f * k, 40f), tab == _tab ? new Color(0.36f, 0.27f, 0.1f) : new Color(0.13f, 0.15f, 0.19f, 1f),
                    () => { _tab = tab; if (tab != 1) _gearSide = 0; Redraw(); }, out var t);
                t.text = L10n.T(keys[k], count);
                t.fontSize = 17f;
                if (tab == _tab) Ring(b.transform, new Vector2(150f, 36f), Hud.Accent);
            }
            if (Smith)
            {
                // One click for every merge that's possible (Owen: "一鍵合成").
                int merges = v.Mergeable();
                var mb = Btn(_tabsLayer, new Vector2(150f, 34f), GridTopLeft + new Vector2(760f, 40f), new Color(0.24f, 0.34f, 0.52f, 1f), () => { Send(VaultAction.MergeAll); AutoTest.Count("smith_merge_all"); }, out var mt);
                mt.text = L10n.T("smithMergeAll", merges);
                mt.fontSize = 16f;
                mb.interactable = merges > 0;
            }
            if (_tab == 1 && !Smith)
            {
                // 全部 / 給人 / 給塔: the plain gear tab mixes the team's tower buffs with what one player wears.
                for (int side = 0; side < 3; side++)
                {
                    int sd = side;
                    var sb = Btn(_tabsLayer, new Vector2(56f, 34f), GridTopLeft + new Vector2(740f + 60f * side, 40f),
                        sd == _gearSide ? new Color(0.36f, 0.27f, 0.1f) : new Color(0.13f, 0.15f, 0.19f, 1f),
                        () => { _gearSide = sd; Redraw(); }, out var st);
                    st.text = L10n.T(side == 0 ? "vaultGearAll" : side == 1 ? "vaultGearPeople" : "vaultGearTowers");
                    st.fontSize = 15f;
                }
            }
        }

        // The slots' columns and size: as big as the well holds them (90 at most), more to a row once there are many (the
        // vault grows to 130).
        private static (int cols, float pitch) Layout(int slots)
        {
            var best = (cols: Columns, pitch: 0f);
            for (int c = Columns; c <= 20; c++)
            {
                float p = Mathf.Min(90f, Mathf.Min(GridWidth / c, GridHeight / Mathf.Max(1, Mathf.CeilToInt(slots / (float)c))));
                if (p > best.pitch + 0.5f) best = (c, p);
            }
            return best;
        }

        private void DrawGrid(VaultData v)
        {
            Clear(_gridLayer);
            // The shop: one of every kind of weapon, common, with its price, then active gear (K) and the game's explosives;
            // mods: every kind of weapon, with your levels.
            var piles = Shop || Modding ? GearCatalog.WeaponKinds.Select(g => new Pile { Gear = g, Rarity = Rarity.Common, Count = 1 }).Concat(Shop ? ShopExtras() : new Pile[0]).ToList()
                                        : Piles(v).Where(p => InTab(p)).ToList();
            // At the smithy, the weapons you carry show too (marked), so three in hand can go on the bench.
            if (Smith && _tab == 0)
                piles.AddRange(Carried().GroupBy(x => x).Where(gp => v.Count(gp.Key) == 0)
                    .Select(gp => new Pile { Gear = gp.Key.Item1, Rarity = gp.Key.Item2, Stars = gp.Key.Stars, Count = gp.Count() }));
            // At the quartermaster's "all" shows every slot of the vault, the empty ones too; the other tabs (and the bench)
            // only what's there.
            int slots = !Smith && _tab == 0 ? Mathf.Max(v.StorageSlots, piles.Count) : Mathf.Max(piles.Count, Columns);
            var (cols, pitch) = Layout(slots);
            float size = pitch - 8f;
            for (int i = 0; i < slots; i++)
            {
                Vector2 pos = GridTopLeft + new Vector2(pitch * (i % cols) + pitch / 2f, -pitch * (i / cols) - pitch / 2f);
                if (i >= piles.Count)
                {
                    if (Smith || _tab != 0) continue;
                    var spare = Box(_gridLayer, new Vector2(size, size), pos, new Color(0.09f, 0.1f, 0.13f, 1f), 10f);
                    spare.raycastTarget = true;
                    spare.gameObject.AddComponent<VaultDrop>().Dropped = DroppedOnVault;
                    continue;
                }
                var p = piles[i];
                var tile = PileTile(_gridLayer, pos, size, p, v, count: !Shop && !Modding, stars: !Shop && !Modding);
                if (Modding) tile.color = ModTint; // a kind, not a common one (the playtest: 「改造是只能改造普通的品質嗎」)
                if (Shop && !p.IsItem && !Loot.ForSale(p.Gear.Value, v)) PriceTag(tile.transform, new Vector2(0f, -size / 2f + 12f), 14f);
                else if (Shop) Text(tile.transform, "$" + ShopPriceOf(p, v).ToString("N0"), 14f, new Vector2(size - 6f, 20f), new Vector2(0f, -size / 2f + 11f), TextAlignmentOptions.Center).color = Hud.Gold;
                if (Shop && !p.IsItem && GearCatalog.IsActive(p.Gear.Value)) // a K skill, not a weapon
                    Text(tile.transform, "K", 14f, new Vector2(22f, 20f), new Vector2(-size / 2f + 13f, size / 2f - 11f), TextAlignmentOptions.Center).color = Hud.Accent;
                else if (Modding) ModLevels(tile.transform, size, v, p.Gear.Value);
                else if (Smith && !p.IsItem && GroupCount(v, p.Gear.Value, p.Rarity) == 0) Text(tile.transform, L10n.T("smithOnYou"), 13f, new Vector2(size - 6f, 18f), new Vector2(0f, size / 2f - 10f), TextAlignmentOptions.Center).color = Hud.Accent;
                else tile.gameObject.AddComponent<VaultDrop>().Dropped = DroppedOnVault;
                bool picked = _sel is Pile s && s.Same(p);
                Ring(tile.transform, new Vector2(size, size), picked ? Hud.Accent : Modding ? ModRing : p.IsItem ? (BossTrophies.IsItemId(p.Item) ? Hud.Gold : new Color(1f, 1f, 1f, 0.18f)) : GearCatalog.Color(p.Rarity) * new Color(1f, 1f, 1f, 0.7f));
                var pile = p;
                var h = Hover(tile.gameObject, () => { _hover = pile; _hoverSlot = null; RedrawCard(); }, () => { _hover = null; RedrawCard(); },
                    left: () => Pick(pile), right: () => Quick(pile, Keyboard.current != null && Keyboard.current.shiftKey.isPressed ? 10 : 1), dbl: () => Quick(pile, 1));
                // Drag: gear onto one of your slots, weapons and the game's items onto "take out", anything onto the bench.
                if (Shop || Modding) continue;
                h.BeginDrag = () => BeginDrag(pile, null, tile);
                h.EndDrag = EndDrag;
            }
            if (Modding)
                Text(_gridLayer, L10n.T("modAllRarities"), 19f, new Vector2(GridWidth - 40f, 56f),
                     GridTopLeft + new Vector2(GridWidth / 2f, -pitch * Mathf.CeilToInt(slots / (float)cols) - 40f), TextAlignmentOptions.Center).color = Hud.Gold;
            if (piles.Count == 0)
                Text(_gridLayer, L10n.T(Smith ? "smithEmpty" : _tab == 0 ? "vaultEmptyAll" : "vaultTabEmpty"), 22f, new Vector2(GridWidth - 60f, 80f),
                     GridTopLeft + new Vector2(GridWidth / 2f, -GridHeight / 2f + (Smith || _tab != 0 ? 0f : -40f)), TextAlignmentOptions.Center).color = new Color(0.65f, 0.7f, 0.75f);
        }

        // A slot: its picture (the gear's icon, or the game item's picture), the count in the corner, the stars in the
        // other corner, the colour round it.
        private Image PileTile(Transform parent, Vector2 pos, float size, Pile p, VaultData v, bool count = true, bool stars = true)
        {
            bool trophy = p.IsItem && BossTrophies.IsItemId(p.Item);
            var box = Box(parent, new Vector2(size, size), pos, trophy ? new Color(0.32f, 0.22f, 0.06f, 1f) : p.IsItem ? new Color(0.16f, 0.18f, 0.22f, 1f) : Darken(GearCatalog.Color(p.Rarity), 0.3f), 10f);
            box.raycastTarget = true;
            bool shown = Picture(box.transform, p, size * 0.72f, new Vector2(0f, 4f));
            if (!shown)
            {
                var n = Text(box.transform, NameOf(p), 14f, new Vector2(size - 8f, size - 16f), new Vector2(0f, 4f), TextAlignmentOptions.Center);
                n.enableAutoSizing = true; n.fontSizeMin = 9f; n.fontSizeMax = 15f;
            }
            if (!p.IsItem && v.PlusOf(p.Gear.Value, p.Rarity, Loot.MyKey) is int plus && plus > 0) // gear's enhancement, a weapon's your mods
                Text(box.transform, "+" + plus, 14f, new Vector2(40f, 18f), new Vector2(size / 2f - 22f, size / 2f - 11f), TextAlignmentOptions.Right).color = GearCatalog.IsWeapon(p.Gear.Value) ? Hud.Gold : GearCatalog.Color(p.Rarity);
            if (count)
            {
                var c = Text(box.transform, "×" + p.Count, 15f, new Vector2(size - 6f, 20f), new Vector2(0f, -size / 2f + 11f), TextAlignmentOptions.Right);
                c.fontStyle = FontStyles.Bold;
                c.outlineWidth = 0.2f; c.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            }
            if (stars && !p.IsItem) StarBadge(box.transform, size, p.Stars);
            return box;
        }

        // The thing's picture: gear and weapons from Icons/, the game's own items as ItemIcons takes them (false: none yet).
        private static bool Picture(Transform parent, Pile p, float size, Vector2 at)
        {
            if (!p.IsItem) return Hud.Picture(parent, "Gear" + p.Gear.Value, new Vector2(0.5f, 0.5f), at, new Vector2(size, size));
            var sp = ItemIcons.Of(p.Item);
            if (!sp) return false;
            var img = Hud.Node(parent, "Picture", new Vector2(0.5f, 0.5f), at, new Vector2(size, size)).gameObject.AddComponent<Image>();
            img.sprite = sp;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return true;
        }

        private void DrawCapacity(VaultData v)
        {
            bool full = v.SlotsUsed >= v.StorageSlots;
            _capacity.text = $"{L10n.T("vaultStorage")}  <color={Hud.Hex(full ? Hud.Bad : v.SlotsUsed >= v.StorageSlots * 0.85f ? Hud.Gold : Hud.Ink)}><b>{v.SlotsUsed}</b></color><color={Hud.Hex(Hud.Muted)}> / {L10n.T("vaultSlots", v.StorageSlots)}</color>";
            var price = PriceOf(v, Expansion.Storage);
            _expandBtn.gameObject.SetActive(!Smith && price != null);
            if (price is (int scales, int best))
            {
                _expandBtn.interactable = CanBuy(Expansion.Storage);
                _expandText.text = v.Best >= best ? L10n.T("vaultExpand", GearCatalog.StorageStep, scales) : L10n.T("vaultExpandNeeds", GearCatalog.StorageStep, best);
            }
        }

        // ---------- the quartermaster: a one-click sale under the slots (Owen: "一鍵賣（可以按類別或品質）") ----------

        // What's in the tab (VaultData.SellAll), one colour or every colour (4); a second click 0.4-5 s later sells (a double click does not).
        private int? _hoverSell;
        private int _sellArmed = -1;      // tab * 10 + colour
        private float _sellArmedUntil, _sellArmedAt;
        private static Rarity? ColourOf(int c) => c < 4 ? (Rarity)c : (Rarity?)null;

        private void DrawSale(VaultData v)
        {
            if (_gearSide != 0) { Text(_gridLayer, L10n.T("vaultSellAllOnly"), 17f, new Vector2(GridWidth - 40f, 30f), new Vector2(GridTopLeft.x + GridWidth / 2f, GridTopLeft.y - GridHeight - 92f), TextAlignmentOptions.Center).color = Hud.Muted; return; }
            float y = GridTopLeft.y - GridHeight - 92f;
            Text(_gridLayer, L10n.T("vaultSellTitle"), 19f, new Vector2(110f, 36f), new Vector2(GridTopLeft.x + 55f, y), TextAlignmentOptions.Left).color = Hud.Muted;
            for (int c = 0; c <= 4; c++)
            {
                if (c < 4 && _tab == 3) continue; // the game's items have no colour
                int colour = c, code = _tab * 10 + c, n = v.SaleOf(_tab, ColourOf(c)).count;
                bool armed = _sellArmed == code && Time.unscaledTime < _sellArmedUntil;
                var b = Btn(_gridLayer, new Vector2(148f, 36f), new Vector2(GridTopLeft.x + 194f + 156f * c, y),
                    armed ? new Color(0.6f, 0.2f, 0.16f) : c < 4 ? Darken(GearCatalog.Color((Rarity)c), 0.32f) : new Color(0.24f, 0.2f, 0.12f), () => Sell(code, colour), out var t);
                t.text = armed ? L10n.T("vaultSellAgain", n) : $"{(c < 4 ? GearCatalog.RarityName((Rarity)c) : L10n.T("vaultSellEvery"))} ×{n}";
                t.fontSize = 16f;
                if (c < 4 && !armed) t.color = GearCatalog.Color((Rarity)c);
                b.interactable = n > 0;
                Hover(b.gameObject, () => { _hoverSell = colour; RedrawCard(); }, () => { _hoverSell = null; RedrawCard(); });
            }
            // One click, everything: its own row, because a sixth 148 px button at the old x ran under the right-hand
            // loadout panel and was cut off (Owen: "一鍵全賣也是沒辦法完整顯示").
            var allBtn = Btn(_gridLayer, new Vector2(330f, 34f), new Vector2(GridTopLeft.x + GridWidth / 2f, y - 44f),
                new Color(0.62f, 0.18f, 0.13f), () =>
                {
                    PreviewSellAll(0, null);
                    Send(VaultAction.SellAll, slot: 0, count: 1);
                    Click("Buy_01");
                    AutoTest.Count("vault_sell_everything");
                }, out var allText);
            allText.text = L10n.T("vaultSellEverything");
            allText.fontSize = 17f;
            Hover(allBtn.gameObject, () => { _hoverSell = 5; RedrawCard(); }, () => { _hoverSell = null; RedrawCard(); });
        }

        // One-click sales clear the local grid at once; the host re-sends the real vault after the sale.
        private static void PreviewSellAll(int tab, Rarity? colour)
        {
            var v = Loot.Instance ? Loot.Instance.ClientVault : null;
            if (v == null) return;
            v.SellAll(tab, colour, _ => 0);
            if (Instance) { Instance._syncPending = true; Instance._syncAt = Time.unscaledTime + 0.8f; }
        }

        private void Sell(int code, int colour)
        {
            bool armed = _sellArmed == code && Time.unscaledTime < _sellArmedUntil;
            if (!armed) { _sellArmed = code; _sellArmedAt = Time.unscaledTime; _sellArmedUntil = Time.unscaledTime + 5f; Click(); Redraw(); return; }
            if (Time.unscaledTime - _sellArmedAt < 0.4f) return; // a double click must not count as the confirmation
            _sellArmed = -1;
            var c = ColourOf(colour);
            PreviewSellAll(_tab, c);
            Send(VaultAction.SellAll, r: c ?? Rarity.Common, slot: _tab, count: c == null ? 1 : 0);
            Click("Buy_01");
            AutoTest.Count("vault_sell_all");
        }

        // ---------- the quartermaster: your loadout ----------

        private List<GearKey> Mine(VaultData v) => Player.LocalPlayer && v.Personal.TryGetValue(Loot.KeyOf(Player.LocalPlayer), out var l) ? l : new List<GearKey>();

        private List<SlotRef> SlotsOf(VaultData v)
        {
            var list = new List<SlotRef>();
            for (int i = 0; i < v.SlotsOf(0); i++)
                list.Add(new SlotRef { Kind = 0, Visual = i, List = i, Item = i < v.Fortress.Count ? v.Fortress[i] : (GearKey?)null });
            var mine = Mine(v);
            var have = mine.Select((x, i) => (x, i)).Where(t => KindOf(t.x.Item1) == 1).ToList(); // personal (active gear isn't worn: Actives)
            for (int s = 0; s < v.SlotsOf(1); s++)
                list.Add(new SlotRef { Kind = 1, Visual = s, List = s < have.Count ? have[s].i : -1, Item = s < have.Count ? have[s].x : (GearKey?)null });
            return list;
        }

        private const float Slot = 92f;
        private static Vector2 SlotPos(int kind, int visual) =>
            kind == 0 ? new Vector2(RightX - 205f + 102f * (visual % 5), 250f - 100f * (visual / 5))
          : kind == 1 ? new Vector2(RightX - 205f + 102f * visual, 25f) : new Vector2(RightX + 233f, 25f);

        // Active items are a separate strip, but must stay inside the right panel: the old far-right column was
        // half outside the panel/window on 16:10 screens. This row sits above the loadout title.
        private void DrawActiveStrip(VaultData v)
        {
            var owned = Actives.Owned(v);
            float y = 390f, x0 = RightX - 90f;
            Box(_rightLayer, new Vector2(RightWidth - 40f, 64f), new Vector2(RightX, y), new Color(0.08f, 0.10f, 0.14f, 0.92f), 14f).raycastTarget = false;
            Text(_rightLayer, L10n.T("vaultRowActiveCount", owned.Count), 15f, new Vector2(190f, 58f), new Vector2(RightX - 205f, y), TextAlignmentOptions.Left).color = Hud.Muted;
            int shown = 0;
            foreach (var (g, r, top) in owned)
            {
                if (shown >= 4) break;
                int total = 0;
                for (int ri = 0; ri < 4; ri++)
                    for (int si = 0; si <= GearCatalog.MaxStars; si++) total += v.Count(g, (Rarity)ri, si);
                var pile = new Pile { Gear = g, Rarity = r, Count = total, Stars = top };
                var tile = PileTile(_rightLayer, new Vector2(x0 + shown * 64f, y), 54f, pile, v, count: true);
                Ring(tile.transform, new Vector2(54f, 54f), GearCatalog.Color(r));
                Hover(tile.gameObject, () => { _hover = pile; _hoverSlot = null; RedrawCard(); }, () => { _hover = null; RedrawCard(); },
                    left: () => { _sel = pile; RedrawCard(); });
                shown++;
            }
            if (shown == 0)
                Text(_rightLayer, L10n.T("vaultActiveEmpty"), 13f, new Vector2(500f, 44f), new Vector2(RightX + 55f, y), TextAlignmentOptions.Center).color = Hud.Muted;
        }

        private void DrawLoadout(VaultData v)
        {
            var box = Box(_rightLayer, new Vector2(RightWidth, 400f), new Vector2(RightX, 150f), Hud.Raised, 18f);
            box.raycastTarget = false;
            // Two clear zones: tower gear on top, personal gear underneath (Owen: "分給人給塔上下分區").
            Box(_rightLayer, new Vector2(RightWidth - 30f, 260f), new Vector2(RightX, 220f), new Color(0.12f, 0.15f, 0.20f, 0.75f), 14f).raycastTarget = false;
            Box(_rightLayer, new Vector2(RightWidth - 30f, 100f), new Vector2(RightX, 35f), new Color(0.16f, 0.14f, 0.10f, 0.75f), 14f).raycastTarget = false;
            Box(_rightLayer, new Vector2(RightWidth - 40f, 2f), new Vector2(RightX, 104f), new Color(1f, 1f, 1f, 0.10f), 0f).raycastTarget = false;
            Text(_rightLayer, L10n.T("vaultLoadoutTitle"), 24f, new Vector2(RightWidth - 40f, 32f), new Vector2(RightX, 330f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
            Text(_rightLayer, L10n.T("vaultRowFortressCount", v.Fortress.Count, v.SlotsOf(0)), 17f, new Vector2(RightWidth - 40f, 26f), new Vector2(RightX, 302f), TextAlignmentOptions.Left).color = Hud.Accent;
            int personalUsed = Mine(v).Count(x => KindOf(x.Item1) == 1);
            Text(_rightLayer, L10n.T("vaultRowPersonalCount", personalUsed, v.SlotsOf(1)), 17f, new Vector2(420f, 26f), new Vector2(RightX - 100f, 82f), TextAlignmentOptions.Left).color = Hud.Gold;

            bool dragging = _dragPile is Pile dp;
            int dragKind = dragging ? KindOf(_dragPile.Value.Gear.Value) : -1;
            foreach (var s in SlotsOf(v))
            {
                var sref = s;
                bool fits = dragKind == s.Kind;
                GearKey? worn = s.Item;
                Image tile;
                if (worn is GearKey wk)
                {
                    tile = PileTile(_rightLayer, SlotPos(s.Kind, s.Visual), Slot, new Pile { Gear = wk.Item1, Rarity = wk.Item2, Stars = wk.Stars, Count = 1 }, v, count: false);
                }
                else
                {
                    tile = Box(_rightLayer, new Vector2(Slot, Slot), SlotPos(s.Kind, s.Visual), fits ? new Color(0.2f, 0.32f, 0.22f, 1f) : new Color(0.1f, 0.12f, 0.15f, 1f), 12f);
                    tile.raycastTarget = true;
                    Text(tile.transform, "+", 38f, new Vector2(Slot, Slot), Vector2.zero, TextAlignmentOptions.Center).color = new Color(1f, 1f, 1f, fits ? 0.7f : 0.22f);
                }
                Ring(tile.transform, new Vector2(Slot, Slot), fits ? Hud.Good : worn is GearKey wk2 ? GearCatalog.Color(wk2.Item2) : new Color(1f, 1f, 1f, 0.15f));
                var h = Hover(tile.gameObject, () => { _hoverSlot = sref; _hover = null; RedrawCard(); }, () => { _hoverSlot = null; RedrawCard(); },
                    left: () => { if (sref.Item is GearKey k) { _sel = Selection(v, k.Item1, k.Item2, k.Stars); RedrawCard(); } },
                    right: () => { if (sref.Item.HasValue) Unequip(sref); });
                tile.gameObject.AddComponent<VaultDrop>().Dropped = () => { if (_dragPile is Pile p && KindOf(p.Gear.Value) == sref.Kind) EquipInto(sref, p.Gear.Value, p.Rarity, p.Stars); };
                if (sref.Item.HasValue) { h.BeginDrag = () => BeginDrag(null, sref, tile); h.EndDrag = EndDrag; }
            }
            if (v.SlotsOf(0) < 10) BuyTile(Expansion.TeamSlot, SlotPos(0, v.SlotsOf(0)));
            BuyTile(Expansion.PersonalSlot, SlotPos(1, v.SlotsOf(1)));

            // Active gear is consumable, not worn: give it its own vertical strip instead of a one-line "nothing to wear" note.
            // Each tile is the best colour of that kind and the total stock; K uses the picked one, Shift+K switches.
            DrawActiveStrip(v);

            // Who wears what (the team), under the slots.
            var others = v.Personal.Where(kv => !Player.LocalPlayer || kv.Key != Loot.KeyOf(Player.LocalPlayer)).Where(kv => kv.Value.Count > 0)
                .Select(kv => (v.Names.TryGetValue(kv.Key, out var n) ? n : "?") + "：" + string.Join("、", kv.Value.Select(x => GearCatalog.Label(x.Item1, x.Item2) + (x.Stars > 0 ? " " + new string('★', x.Stars) : "")))).ToList();
            var team = Text(_rightLayer, others.Count > 0 ? L10n.T("vaultTeam") + "  " + string.Join("　", others) : "", 14f, new Vector2(RightWidth - 40f, 40f), new Vector2(RightX, -50f), TextAlignmentOptions.TopLeft);
            team.color = new Color(0.75f, 0.8f, 0.85f);
            team.enableAutoSizing = true; team.fontSizeMin = 10f; team.fontSizeMax = 14f;
        }

        // A locked slot at the end of a row: what it costs (or how far you have to get first). Click to buy it.
        private void BuyTile(Expansion e, Vector2 pos)
        {
            var v = Loot.Instance.ClientVault;
            if (!(PriceOf(v, e) is (int scales, int best))) return;
            bool can = CanBuy(e);
            var box = Box(_rightLayer, new Vector2(Slot, Slot), pos, can ? new Color(0.26f, 0.2f, 0.08f, 1f) : new Color(0.08f, 0.09f, 0.11f, 1f), 12f);
            box.raycastTarget = true;
            Text(box.transform, "+", 30f, new Vector2(Slot, 44f), new Vector2(0f, 14f), TextAlignmentOptions.Center).color = new Color(1f, 1f, 1f, can ? 0.85f : 0.35f);
            var t = Text(box.transform, v.Best >= best ? L10n.T("vaultBuyPrice", scales) : L10n.T("vaultBuyNeeds", best), 14f, new Vector2(Slot - 8f, 36f), new Vector2(0f, -24f), TextAlignmentOptions.Center);
            t.color = can ? Hud.Gold : Hud.Muted;
            t.enableAutoSizing = true; t.fontSizeMin = 9f; t.fontSizeMax = 14f;
            Ring(box.transform, new Vector2(Slot, Slot), can ? Hud.Gold * new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.12f));
            Hover(box.gameObject, () => { _hoverBuy = e; RedrawCard(); }, () => { _hoverBuy = null; RedrawCard(); }, left: () => { if (can) Buy(e); });
        }

        private static (int scales, int best)? PriceOf(VaultData v, Expansion e) => GearCatalog.NextPrice(e, v.BoughtOf(e));
        private static bool CanBuy(Expansion e)
        {
            var v = Loot.Instance.ClientVault;
            return PriceOf(v, e) is var p && p != null && v.Scales >= p.Value.scales && v.Best >= p.Value.best;
        }
        private static void Buy(Expansion e) { Send(VaultAction.Expand, slot: (int)e, count: Loot.Instance.ClientVault.BoughtOf(e)); Click("Buy_01"); }

        // ---------- the smith: the bench ----------

        private void DrawBench(VaultData v)
        {
            var bench = Box(_rightLayer, new Vector2(RightWidth, 400f), new Vector2(RightX, 150f), Hud.Raised, 18f);
            bench.raycastTarget = true;
            // Dragged onto the bench: look at that piece's group at the star it was dropped with.
            bench.gameObject.AddComponent<VaultDrop>().Dropped = () => { if (_dragPile is Pile dp && !dp.IsItem) { _sel = Selection(Loot.Instance.ClientVault, dp.Gear.Value, dp.Rarity, dp.Stars); _pickedStar[(dp.Gear.Value, dp.Rarity)] = dp.Stars; } };
            Text(_rightLayer, L10n.T("smithBench"), 24f, new Vector2(RightWidth - 40f, 32f), new Vector2(RightX, 330f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
            if (!(_sel is Pile p) || p.IsItem)
            {
                Text(_rightLayer, L10n.T("smithBenchEmpty"), 22f, new Vector2(RightWidth - 80f, 120f), new Vector2(RightX, 170f), TextAlignmentOptions.Center).color = new Color(0.65f, 0.7f, 0.75f);
                DepositCarried(v);
                return;
            }
            var g = p.Gear.Value;
            var r = p.Rarity;
            var counts = StarCountsOf(v, g, r);
            int star = Mathf.Clamp(p.Stars, 0, GearCatalog.MaxStars);
            int haveAt = counts[star];
            int haveAll = 0;
            for (int s = 0; s < counts.Length; s++) haveAll += counts[s];
            // The ones you carry count for the forge too (they go into the vault first): the playtest wanted hands + vault read.
            // A carried weapon keeps its own star (WeaponItems.StarsOf), so it feeds the substack it belongs to.
            int carried = GearCatalog.IsWeapon(g) ? Carried().Count(x => x.Item1 == g && x.Item2 == r && x.Stars == star) : 0, pool = haveAt + carried;
            bool canMerge = star < GearCatalog.MaxStars || r < Rarity.Legendary;
            string chain = MergeChain(r, star) + (canMerge ? $"  <color={Hud.Hex(pool >= 3 ? Hud.Good : Hud.Muted)}>{pool}/3</color>" : "");
            // Which substack the bench is looking at: a row of four star buttons by the title (the highest by default).
            for (int s = 0; s <= GearCatalog.MaxStars; s++)
            {
                int sd = s;
                bool on = sd == star;
                var sb = Btn(_rightLayer, new Vector2(70f, 30f), new Vector2(RightX + 130f + 76f * s, 296f),
                    on ? new Color(0.36f, 0.27f, 0.1f) : new Color(0.13f, 0.15f, 0.19f, 1f),
                    () => { if (_sel is Pile cur) { cur.Stars = sd; _sel = cur; _pickedStar[(cur.Gear.Value, cur.Rarity)] = sd; Redraw(); } }, out var st);
                st.text = StarName(sd); // 0★ / ★ / ★★ / ★★★; the colours below say whether the substack has any
                st.fontSize = 14f;
                if (counts[sd] == 0) st.color = Hud.Muted;
                else st.color = sd == 0 ? Hud.Ink : Hud.Gold;
                if (on) Ring(sb.transform, new Vector2(70f, 30f), Hud.Accent);
            }
            // What three of the selected substack become, right by the title.
            var chainText = Text(_rightLayer, chain, 16f, new Vector2(340f, 26f), new Vector2(RightX - 130f, 296f), TextAlignmentOptions.Left);
            chainText.enableAutoSizing = true; chainText.fontSizeMin = 10f; chainText.fontSizeMax = 16f;
            chainText.enableWordWrapping = false;
            // Three on the bench (as many as there are, the rest ghosted), an arrow, what comes out.
            for (int i = 0; i < 3; i++)
            {
                var tile = PileTile(_rightLayer, new Vector2(RightX - 250f + 100f * i, 230f), 86f, new Pile { Gear = g, Rarity = r, Stars = star, Count = 1 }, v, count: false);
                Ring(tile.transform, new Vector2(86f, 86f), GearCatalog.Color(r));
                if (i >= pool) foreach (var gr in tile.GetComponentsInChildren<Graphic>()) gr.color *= new Color(1f, 1f, 1f, 0.25f);
            }
            Text(_rightLayer, "→", 52f, new Vector2(60f, 60f), new Vector2(RightX + 75f, 230f), TextAlignmentOptions.Center).color = Hud.Accent;
            _benchOut = Node(_rightLayer, "BenchOut");
            DrawBenchOutput(v, g, r, star);
            Text(_rightLayer, $"{GearCatalog.RarityName(r)} {GearCatalog.Name(g)}  {StarMark(star)}  ×{haveAt}" + (carried > 0 ? $"  <size=85%>{L10n.T("smithCarriedCount", carried)}</size>" : ""), 18f, new Vector2(RightWidth - 40f, 26f), new Vector2(RightX, 165f), TextAlignmentOptions.Center).color = GearCatalog.Color(r);

            // The bench's work: forge three into one, rank one up with scales, enhance a legendary, melt one down; and take a
            // weapon out (it lands on the ground before the smithy).
            int lvl = v.EnhanceOf(g);
            int ascend = GearCatalog.AscendCost(r);
            int salvage = Mathf.RoundToInt(GearCatalog.SalvageScales[(int)r] * (1f + 0.5f * star));
            BenchBtn("merge", new Vector2(RightX - 160f, 110f), canMerge && pool >= 3, canMerge ? L10n.T("smithMerge", pool) : L10n.T("vaultMaxed"), new Color(0.26f, 0.42f, 0.78f),
                () =>
                {
                    for (int i = 0; i < Mathf.Min(carried, Mathf.Max(0, 3 - haveAt)); i++) Send(VaultAction.Deposit, g, r, stars: star); // the carried ones in first (the host takes them in order)
                    Send(VaultAction.Merge, g, r, stars: star);
                    // The last three: what they made stays on the bench (only if the local preview actually consumed them: the
                    // carried ones aren't previewed, so wait for the host's vault in that case).
                    if (pool <= 3 && Loot.Instance.ClientVault.Count(g, r, star) == 0) WatchMade(g, r, star);
                });
            if (r == Rarity.Legendary && GearCatalog.IsWeapon(g))
                BenchBtn(null, new Vector2(RightX + 160f, 110f), true, L10n.T("smithToMods"), new Color(0.55f, 0.4f, 0.1f), () => { _tab = ModTab; _sel = new Pile { Gear = g, Rarity = Rarity.Common, Count = 1 }; Redraw(); });
            else if (r == Rarity.Legendary)
                BenchBtn("enhance", new Vector2(RightX + 160f, 110f), haveAll > 0 && lvl < GearCatalog.MaxEnhance && v.Scales >= GearCatalog.EnhanceCosts[Mathf.Min(lvl, GearCatalog.MaxEnhance - 1)],
                    lvl >= GearCatalog.MaxEnhance ? L10n.T("vaultEnhanceMax") : L10n.T("vaultEnhance", lvl + 1, GearCatalog.EnhanceCosts[lvl]), new Color(0.55f, 0.4f, 0.1f), () => Send(VaultAction.Enhance, g, r, count: lvl));
            else
                // The quartermaster's scale rank-up only knows the 0★ pieces (VaultData.Apply Ascend): offer it on the 0★ substack.
                BenchBtn("ascend", new Vector2(RightX + 160f, 110f), ascend >= 0 && star == 0 && counts[0] > 0 && v.Scales >= ascend,
                    ascend >= 0 ? L10n.T("vaultAscend", ascend) : L10n.T("vaultNoAscend"), new Color(0.52f, 0.36f, 0.12f), () => { Send(VaultAction.Ascend, g, r, stars: 0); if (Loot.Instance.ClientVault.Count(g, r, 0) == 0) WatchMade(g, r, GearCatalog.MaxStars); });
            BenchBtn("salvage", new Vector2(RightX - 160f, 55f), haveAt > 0, L10n.T("vaultSalvage", salvage), new Color(0.36f, 0.3f, 0.3f),
                () => Send(VaultAction.Salvage, g, r, stars: star));
            if (r == Rarity.Legendary) Text(_rightLayer, L10n.T(GearCatalog.IsWeapon(g) ? "smithModsNote" : "smithEnhanceNote"), 15f, new Vector2(RightWidth - 40f, 22f), new Vector2(RightX, 6f), TextAlignmentOptions.Center).color = Hud.Muted; // under the buttons
            if (GearCatalog.IsWeapon(g))
                BenchBtn(null, new Vector2(RightX + 160f, 55f), haveAll > 0, L10n.T("smithTakeOne"), new Color(0.22f, 0.5f, 0.35f), () => { Send(VaultAction.Take, g, r, slot: 1, stars: star); Click("UnequipItem"); });
            DepositCarried(v);
        }

        // After a merge that used up the selected substack: put the bench on what came out (the next star, or the next
        // rarity at 0★), like the vault showing the new piece.
        private void WatchMade(Gear g, Rarity r, int star)
        {
            if (star < GearCatalog.MaxStars) { _pickedStar[(g, r)] = star + 1; _sel = Selection(Loot.Instance.ClientVault, g, r, star + 1); }
            else if (r < Rarity.Legendary) { _sel = Selection(Loot.Instance.ClientVault, g, r + 1, 0); }
        }

        // ---------- the smith: your own mods on a kind of weapon ----------

        // A mods tile's levels along its bottom (in the smithy's order).
        private static void ModLevels(Transform tile, float size, VaultData v, Gear g)
        {
            var levels = WeaponMods.Shown.Select(k => WeaponMods.Has(g, k) ? v.ModOf(Loot.MyKey, g, k).ToString() : "-").ToList();
            var text = Text(tile, string.Join("·", levels), 14f, new Vector2(size - 6f, 20f), new Vector2(0f, -size / 2f + 11f), TextAlignmentOptions.Center);
            text.color = levels.Any(l => l != "0" && l != "-") ? Hud.Gold : Hud.Muted;
            text.fontStyle = FontStyles.Bold;
        }

        // The kind picked on the left: its four lines (WeaponMods.Shown), each with its level, what it does now and next, and
        // the price of the next level (there's no top: Owen "可以都升級到很誇張").
        private void DrawMods(VaultData v)
        {
            var box = Box(_rightLayer, new Vector2(RightWidth, 400f), new Vector2(RightX, 150f), Hud.Raised, 18f);
            box.raycastTarget = true;
            if (!(_sel is Pile p) || p.IsItem || !GearCatalog.IsWeapon(p.Gear.Value))
            {
                Text(_rightLayer, L10n.T("smithTabMods"), 24f, new Vector2(RightWidth - 40f, 32f), new Vector2(RightX, 330f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
                Text(_rightLayer, L10n.T("modPick"), 21f, new Vector2(RightWidth - 80f, 140f), new Vector2(RightX, 170f), TextAlignmentOptions.Center).color = new Color(0.65f, 0.7f, 0.75f);
                return;
            }
            var g = p.Gear.Value;
            ulong me = Loot.MyKey;
            Text(_rightLayer, L10n.T("modTitle", GearCatalog.Name(g)), 24f, new Vector2(RightWidth - 40f, 32f), new Vector2(RightX, 330f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
            for (int row = 0; row < WeaponMods.Shown.Length; row++)
            {
                int t = WeaponMods.Shown[row];
                float y = 266f - 72f * row;
                Box(_rightLayer, new Vector2(RightWidth - 30f, 66f), new Vector2(RightX, y), new Color(0.1f, 0.12f, 0.15f, 1f), 12f);
                var name = Text(_rightLayer, WeaponMods.TrackName(g, t), 20f, new Vector2(150f, 26f), new Vector2(RightX - 225f, y + 14f), TextAlignmentOptions.Left);
                name.fontStyle = FontStyles.Bold;
                if (!WeaponMods.Has(g, t))
                {
                    Text(_rightLayer, L10n.T("modNoMagazine"), 16f, new Vector2(440f, 46f), new Vector2(RightX + 70f, y), TextAlignmentOptions.Left).color = Hud.Muted;
                    name.color = Hud.Muted;
                    continue;
                }
                int lvl = v.ModOf(me, g, t);
                Text(_rightLayer, "Lv" + lvl, 18f, new Vector2(150f, 22f), new Vector2(RightX - 225f, y - 13f), TextAlignmentOptions.Left).color = lvl > 0 ? Hud.Gold : Hud.Muted;
                var what = Text(_rightLayer, $"<color={Hud.Hex(Hud.Muted)}>{WeaponMods.TrackWhat(g, t)}</color>\n{WeaponMods.TrackValue(g, t, lvl)}"
                                + $"  <color={Hud.Hex(Hud.Good)}>→ {WeaponMods.TrackValue(g, t, lvl + 1)}</color>",
                                16f, new Vector2(270f, 58f), new Vector2(RightX - 15f, y), TextAlignmentOptions.Left);
                what.enableAutoSizing = true; what.fontSizeMin = 10f; what.fontSizeMax = 16f;
                int track = t, cost = WeaponMods.Cost(lvl);
                TextMeshProUGUI bt = null; Button b = null;
                b = Btn(_rightLayer, new Vector2(150f, 46f), new Vector2(RightX + 220f, y), new Color(0.55f, 0.4f, 0.1f),
                    () =>
                    {
                        // Do not freeze the row behind a captured `busy` flag: after one host round-trip it used to stay black
                        // and "can't upgrade" until some other vault change happened to redraw it.
                        int cur = Loot.Instance ? Loot.Instance.ClientVault.ModOf(Loot.MyKey, g, track) : lvl;
                        PreviewMod(g, track, cur);
                        if (bt) bt.text = L10n.T("modBuy", cur + 2, WeaponMods.Cost(cur + 1).ToString("N0"));
                        if (what) what.text = $"<color={Hud.Hex(Hud.Muted)}>{WeaponMods.TrackWhat(g, track)}</color>\n{WeaponMods.TrackValue(g, track, cur + 1)}  <color={Hud.Hex(Hud.Good)}>→ {WeaponMods.TrackValue(g, track, cur + 2)}</color>";
                        Send(VaultAction.Mod, g, slot: track, count: cur & 255); Click("Buy_01");
                    }, out bt);
                b.interactable = v.Scales >= cost;
                bt.text = L10n.T("modBuy", lvl + 1, cost.ToString("N0")); // no "SYNCING": the local click already shows the next level
                bt.enableAutoSizing = true; bt.fontSizeMin = 11f; bt.fontSizeMax = 18f;
            }
            var note = Text(_rightLayer, L10n.T("modNote", GearCatalog.Name(g)), 15f, new Vector2(RightWidth - 40f, 40f), new Vector2(RightX, -25f), TextAlignmentOptions.Center);
            note.color = Hud.Muted;
            note.enableAutoSizing = true; note.fontSizeMin = 11f; note.fontSizeMax = 15f;
        }

        // The shop past the weapons: active gear (K: one common into the vault) and the game's explosives (Loot.ShopItems).
        private static IEnumerable<Pile> ShopExtras() =>
            GearCatalog.All.Where(g => GearCatalog.IsActive(g) && !GearCatalog.Retired(g)).Select(g => new Pile { Gear = g, Rarity = Rarity.Common, Count = 1 })
                .Concat(Loot.ShopItems.Select(id => new Pile { Item = id, Count = 1 }));
        private static int ShopPriceOf(Pile p, VaultData v) => p.IsItem ? Loot.ItemPrice(p.Item) : Loot.ShopPrice(p.Gear.Value, v);

        // The smith's shop: what's picked on the left (its card below says what it does), bought for the crew's money.
        private void DrawShop()
        {
            var box = Box(_rightLayer, new Vector2(RightWidth, 400f), new Vector2(RightX, 150f), Hud.Raised, 18f);
            box.raycastTarget = true;
            Text(_rightLayer, L10n.T("smithShopTitle"), 24f, new Vector2(RightWidth - 40f, 32f), new Vector2(RightX, 330f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
            if (!(_sel is Pile p))
            {
                Text(_rightLayer, L10n.T("smithShopPick"), 22f, new Vector2(RightWidth - 80f, 120f), new Vector2(RightX, 200f), TextAlignmentOptions.Center).color = new Color(0.65f, 0.7f, 0.75f);
            }
            else
            {
                var v = Loot.Instance.ClientVault;
                bool item = p.IsItem, weapon = !item && GearCatalog.IsWeapon(p.Gear.Value);
                int price = ShopPriceOf(p, v);
                bool sale = item || Loot.ForSale(p.Gear.Value, v), room = item || Loot.Buyable(p.Gear.Value, v) || !sale, rich = Siege.Instance.ViewCoins >= price;
                var shown = item ? p : new Pile { Gear = p.Gear, Rarity = Rarity.Common, Count = 1 };
                var tile = PileTile(_rightLayer, new Vector2(RightX - 150f, 220f), 110f, shown, v, count: false, stars: false);
                Ring(tile.transform, new Vector2(110f, 110f), item ? new Color(1f, 1f, 1f, 0.25f) : GearCatalog.Color(Rarity.Common));
                var name = Text(_rightLayer, item ? NameOf(p) : GearCatalog.Label(p.Gear.Value, Rarity.Common), 24f, new Vector2(280f, 40f), new Vector2(RightX + 80f, 250f), TextAlignmentOptions.Left);
                name.fontStyle = FontStyles.Bold;
                name.enableAutoSizing = true; name.fontSizeMin = 14f; name.fontSizeMax = 24f;
                if (sale) Text(_rightLayer, "$" + price.ToString("N0"), 30f, new Vector2(280f, 44f), new Vector2(RightX + 80f, 205f), TextAlignmentOptions.Left).color = Hud.Gold;
                else PriceTag(_rightLayer, new Vector2(RightX + 10f, 205f), 30f);
                var b = Btn(_rightLayer, new Vector2(RightWidth - 60f, 52f), new Vector2(RightX, 110f), new Color(0.22f, 0.58f, 0.38f), () =>
                {
                    if (item) Send(VaultAction.BuyItem, item: p.Item); else Send(VaultAction.Buy, p.Gear.Value, Rarity.Common);
                    Click(weapon || item ? "UnequipItem" : "Buy_01");
                }, out var t);
                b.interactable = sale && room && rich;
                t.text = !sale ? L10n.T("smithShopLocked", Loot.UnlockBest(p.Gear.Value)) : !room ? L10n.T("smithShopFull") : !rich ? L10n.T("smithShopPoor")
                       : L10n.T(weapon ? "smithBuy" : "smithBuyOne", price.ToString("N0"));
            }
            Text(_rightLayer, L10n.T("smithShopNote"), 17f, new Vector2(RightWidth - 60f, 60f), new Vector2(RightX, 15f), TextAlignmentOptions.Center).color = Hud.Muted;
        }

        // A special weapon not for sale yet: its price is question marks, black and red, on a paper tag.
        private static void PriceTag(Transform parent, Vector2 pos, float size)
        {
            Box(parent, new Vector2(size * 3.6f, size * 1.25f), pos, new Color(0.94f, 0.89f, 0.76f), size * 0.25f);
            string marks = string.Concat(Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? "<color=#141414>?</color>" : "<color=#e0302a>?</color>"));
            Text(parent, "<color=#141414>$</color>" + marks, size, new Vector2(size * 3.6f, size * 1.25f), pos, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
        }

        // What the bench would make: what the button under the mouse does, else the likeliest job.
        private Transform _benchOut;
        private void DrawBenchOutput(VaultData v, Gear g, Rarity r, int star)
        {
            if (!_benchOut) return;
            Clear(_benchOut);
            star = Mathf.Clamp(star, 0, GearCatalog.MaxStars);
            int haveAt = v.Count(g, r, star);
            int carried = GearCatalog.IsWeapon(g) ? Carried().Count(x => x.Item1 == g && x.Item2 == r && x.Stars == star) : 0;
            bool canMerge = star < GearCatalog.MaxStars || r < Rarity.Legendary;
            string what = _benchHover ?? (canMerge && haveAt + carried >= 3 ? "merge" : star == 0 && r < Rarity.Epic ? "ascend" : r == Rarity.Legendary && !GearCatalog.IsWeapon(g) ? "enhance" : "salvage");
            if (what == "salvage")
            {
                var s = Box(_benchOut, new Vector2(96f, 96f), new Vector2(RightX + 175f, 230f), new Color(0.12f, 0.2f, 0.28f, 1f), 12f);
                Text(s.transform, $"+{Mathf.RoundToInt(GearCatalog.SalvageScales[(int)r] * (1f + 0.5f * star))}\n{L10n.T("scalesName")}", 22f, new Vector2(90f, 90f), Vector2.zero, TextAlignmentOptions.Center);
                return;
            }
            // Forge: the next star of the same colour, or the next colour at 0★ from 3★. Rank up: the next colour at 0★.
            Rarity outR = r;
            int outStar = star;
            if (what == "merge" && star < GearCatalog.MaxStars) outStar = star + 1;
            else if (what == "merge" || what == "ascend") { outR = (Rarity)Mathf.Min((int)Rarity.Legendary, (int)r + 1); outStar = 0; }
            var o = PileTile(_benchOut, new Vector2(RightX + 175f, 230f), 96f, new Pile { Gear = g, Rarity = outR, Stars = outStar, Count = 1 }, v, count: false);
            if (what == "enhance") Text(o.transform, $"+{Mathf.Min(GearCatalog.MaxEnhance, v.EnhanceOf(g) + 1)}", 20f, new Vector2(80f, 24f), new Vector2(0f, -32f), TextAlignmentOptions.Right).color = Hud.Gold;
            Ring(o.transform, new Vector2(96f, 96f), GearCatalog.Color(outR));
        }

        private void BenchBtn(string key, Vector2 pos, bool can, string label, Color c, Action act)
        {
            var b = Btn(_rightLayer, new Vector2(300f, 46f), pos, c, () => { act(); Click(); }, out var t);
            b.interactable = can;
            t.text = label;
            t.fontSize = 18f;
            if (key == null) return;
            Hover(b.gameObject, () => { _benchHover = key; if (_sel is Pile p && !p.IsItem) DrawBenchOutput(Loot.Instance.ClientVault, p.Gear.Value, p.Rarity, p.Stars); },
                () => { _benchHover = null; if (_sel is Pile p && !p.IsItem) DrawBenchOutput(Loot.Instance.ClientVault, p.Gear.Value, p.Rarity, p.Stars); });
        }

        // The fortress weapons you carry, into the vault in one go (throwing them on the anvil does the same).
        private void DepositCarried(VaultData v)
        {
            var carried = Carried();
            var b = Btn(_rightLayer, new Vector2(RightWidth - 60f, 40f), new Vector2(RightX, -40f), new Color(0.26f, 0.42f, 0.78f), () =>
            {
                foreach (var w in Carried()) Send(VaultAction.Deposit, w.Item1, w.Item2, stars: w.Stars);
                Click();
            }, out var t);
            b.interactable = carried.Count > 0;
            t.text = carried.Count > 0 ? L10n.T("smithDepositAll", carried.Count) : L10n.T("smithCarriedNone");
            t.fontSize = 17f;
        }

        // The fortress weapons the local player carries (hotbar and hand), one entry per item, with its stars.
        private static List<GearKey> Carried()
        {
            var me = Player.LocalPlayer;
            var list = new List<GearKey>();
            if (!me || !me.Inventory) return list;
            var items = me.Inventory._items.Values.Where(i => i).ToList();
            var held = me.Holding ? me.Holding.HeldItem : null;
            if (held && !items.Contains(held)) items.Add(held);
            foreach (var i in items) if (!i.IsDestroying && WeaponItems.Is(i, out var g, out var r)) list.Add(new GearKey(g, r, WeaponItems.StarsOf(i)));
            return list;
        }

        // ---------- clicks and drags ----------

        private void Pick(Pile p) { _sel = p; Redraw(); }

        // Right-click (or double-click): gear goes on (quartermaster) or onto the bench (smith); weapons and the game's
        // items come out (n of them) at the desk.
        private void Quick(Pile p, int n)
        {
            if (Shop) { Pick(p); return; } // the shop's goods aren't in the vault
            if (p.IsItem) { Send(VaultAction.TakeItem, slot: Smith ? 1 : 0, item: p.Item, count: n); Click("UnequipItem"); return; }
            var g = p.Gear.Value;
            if (Smith) { Pick(p); return; }
            if (GearCatalog.IsWeapon(g)) { Send(VaultAction.Take, g, p.Rarity, slot: 0, count: n, stars: p.Stars); Click("UnequipItem"); return; }
            if (TargetFor(Loot.Instance.ClientVault, g, p.Rarity) is SlotRef t) EquipInto(t, g, p.Rarity, p.Stars); else Pick(p);
        }

        // Where a piece goes on: an empty slot (the same kind again adds up: GearCatalog.Stacked), else the slot with its own
        // kind in another colour (swapped), else the first of its row that isn't this very piece (swapped; the playtest's
        // single active slot just said "full" when another active was on).
        private SlotRef? TargetFor(VaultData v, Gear g, Rarity r)
        {
            var row = SlotsOf(v).Where(s => s.Kind == KindOf(g)).Cast<SlotRef?>().ToList();
            bool Same(SlotRef? s) => s.Value.Item is GearKey k && k.Item1 == g && k.Item2 == r;
            return row.FirstOrDefault(s => !s.Value.Item.HasValue) ?? row.FirstOrDefault(s => s.Value.Item is GearKey k && k.Item1 == g && !Same(s))
                   ?? row.FirstOrDefault(s => !Same(s));
        }

        // `stars`: which substack goes on (the tile's shown star, default the highest in the vault).
        private void EquipInto(SlotRef s, Gear g, Rarity r, int stars)
        {
            var v = Loot.Instance.ClientVault;
            if (GroupCount(v, g, r) <= 0) return;
            stars = AvailableStar(v, g, r, stars);
            if (s.Item.HasValue) Unequip(s, quiet: true); // swapping: the old one goes back first
            Send(s.Kind == 0 ? VaultAction.EquipFortress : VaultAction.EquipPersonal, g, r, stars: stars);
            Click();
        }

        private void Unequip(SlotRef s, bool quiet = false)
        {
            if (!(s.Item is GearKey k)) return;
            Send(s.Kind == 0 ? VaultAction.UnequipFortress : VaultAction.UnequipPersonal, k.Item1, k.Item2, stars: k.Stars);
            if (!quiet) Click("UnequipItem");
        }

        private Pile? _dragPile;
        private SlotRef? _dragSlot;

        // Equipped gear let go anywhere over the vault's slots comes off; something from the hotbar goes in.
        private void DroppedOnVault()
        {
            if (_dragSlot is SlotRef s) Unequip(s);
            else if (_dragHotbar >= 0) { Send(VaultAction.StoreSlot, slot: _dragHotbar); Click(); AutoTest.Count("hotbar_drag_in"); }
        }

        private void BeginDrag(Pile? pile, SlotRef? slot, Image from, int hotbar = -1)
        {
            EndDrag();
            _dragPile = pile ?? (slot is SlotRef s && s.Item is GearKey k ? (Pile?)new Pile { Gear = k.Item1, Rarity = k.Item2, Stars = k.Stars, Count = 1 } : null);
            _dragSlot = slot;
            _dragHotbar = hotbar;
            if (_dragPile == null) return;
            var p = _dragPile.Value;
            if (hotbar < 0 && slot == null && (p.IsItem || GearCatalog.IsWeapon(p.Gear.Value))) LightHotbar(true); // the hotbar takes it too
            if (!Smith && slot == null && hotbar < 0 && (p.IsItem || GearCatalog.IsWeapon(p.Gear.Value)))
            {
                // Weapons and the game's items can't be worn: let go over here and one comes out at the door.
                var zone = Box(_dragLayer, new Vector2(RightWidth - 40f, 300f), new Vector2(RightX, -235f), new Color(0.12f, 0.24f, 0.18f, 0.96f), 18f);
                zone.raycastTarget = true;
                Ring(zone.transform, new Vector2(RightWidth - 40f, 300f), Hud.Good);
                Text(zone.transform, L10n.T("vaultDropTake"), 26f, new Vector2(RightWidth - 100f, 200f), Vector2.zero, TextAlignmentOptions.Center).color = Hud.Good;
                var taken = p;
                zone.gameObject.AddComponent<VaultDrop>().Dropped = () => Quick(taken, Keyboard.current != null && Keyboard.current.shiftKey.isPressed ? 10 : 1);
                _zone = zone.gameObject;
            }
            _ghost = Box(_dragLayer, new Vector2(80f, 80f), Vector2.zero, p.IsItem ? new Color(0.16f, 0.18f, 0.22f, 1f) : Darken(GearCatalog.Color(p.Rarity), 0.35f), 10f);
            Picture(_ghost.transform, p, 64f, Vector2.zero);
            foreach (var gr in _ghost.GetComponentsInChildren<Graphic>()) { gr.raycastTarget = false; gr.color *= new Color(1f, 1f, 1f, 0.85f); }
            if (!Smith && slot == null && !p.IsItem && !GearCatalog.IsWeapon(p.Gear.Value)) { Clear(_rightLayer); DrawLoadout(Loot.Instance.ClientVault); } // light up where it fits
        }

        // Called by the tile when the drag ends, after any drop target has had it.
        private void EndDrag()
        {
            bool was = _dragPile != null;
            _dragPile = null;
            _dragSlot = null;
            _dragHotbar = -1;
            LightHotbar(false);
            if (_ghost) Destroy(_ghost.gameObject);
            _ghost = null;
            if (_zone) Destroy(_zone);
            _zone = null;
            if (was && IsOpen) Redraw();
        }

        // ---------- the card ----------

        private void RedrawCard()
        {
            if (!_root) return;
            Clear(_cardLayer);
            var v = Loot.Instance.ClientVault;
            var box = Box(_cardLayer, new Vector2(RightWidth, 360f), new Vector2(RightX, -235f), Hud.Raised, 18f);
            box.raycastTarget = false;
            var c = box.transform;
            if (_hoverBuy is Expansion he)
            {
                var pr = PriceOf(v, he);
                Text(c, L10n.T("expand" + he), 26f, new Vector2(RightWidth - 40f, 36f), new Vector2(0f, 160f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
                Text(c, pr is (int sc, int be) ? (v.Best >= be ? L10n.T("vaultBuyPrice", sc) : L10n.T("vaultBuyNeeds", be)) + $"  <color={Hud.Hex(Hud.Muted)}>{L10n.T("vaultYouHave", v.Scales, v.Best)}</color>" : L10n.T("vaultExpandMax"),
                     18f, new Vector2(RightWidth - 40f, 28f), new Vector2(0f, 126f), TextAlignmentOptions.Left);
                Text(c, L10n.T("expand" + he + "_d"), 19f, new Vector2(RightWidth - 40f, 200f), new Vector2(0f, 0f), TextAlignmentOptions.TopLeft);
                return;
            }
            if (_hoverSell is int hc)
            {
                var colour = ColourOf(hc);
                var (money, scales, count) = v.SaleOf(_tab, colour);
                int items = colour == null && (_tab == 0 || _tab == 3) ? v.ItemCount.Values.Sum() : 0;
                Text(c, hc >= 4 ? L10n.T("vaultSellEverything") : L10n.T("vaultSellCardTitle", colour is Rarity cr ? GearCatalog.RarityName(cr) : L10n.T("vaultSellEvery"), count), 26f, new Vector2(RightWidth - 40f, 36f), new Vector2(0f, 160f), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
                var got = new List<string>();
                if (money > 0) got.Add(L10n.T("vaultSellMoney", money.ToString("N0")));
                if (scales > 0) got.Add(L10n.T("vaultSellScales", scales));
                if (items > 0) got.Add(L10n.T("vaultSellItems", items));
                got.Add($"<color={Hud.Hex(Hud.Muted)}>{L10n.T("vaultSellNote")}</color>");
                Text(c, string.Join("\n", got), 20f, new Vector2(RightWidth - 40f, 240f), new Vector2(0f, 0f), TextAlignmentOptions.TopLeft);
                return;
            }
            Pile? show = _hover;
            bool equipped = false;
            if (show == null && _hoverSlot is SlotRef hs && hs.Item is GearKey hk) { show = new Pile { Gear = hk.Item1, Rarity = hk.Item2, Stars = hk.Stars, Count = 1 }; equipped = true; }
            if (show == null) show = _sel;
            if (!(show is Pile p))
            {
                Text(c, L10n.T(Smith ? "smithCardEmpty" : "vaultCardEmpty"), 21f, new Vector2(RightWidth - 60f, 300f), Vector2.zero, TextAlignmentOptions.Center).color = new Color(0.65f, 0.7f, 0.75f);
                return;
            }
            // The shop sells commons and a mod is on the kind, not a colour: neither has a star to show.
            bool showStars = !p.IsItem && !Shop && !Modding;
            int selectedStar = showStars ? Mathf.Clamp(p.Stars, 0, GearCatalog.MaxStars) : 0;
            // The picture, the name and kind, then what it does (and the next rarity, the legendary special).
            var icon = PileTile(c, new Vector2(-RightWidth / 2f + 70f, 112f), 100f, new Pile { Gear = p.Gear, Rarity = p.Rarity, Stars = selectedStar, Item = p.Item, Count = Total(v, p) }, v, stars: showStars);
            if (Modding) icon.color = ModTint;
            bool trophy = p.IsItem && BossTrophies.IsItemId(p.Item);
            Ring(icon.transform, new Vector2(100f, 100f), Modding ? ModRing : p.IsItem ? (trophy ? Hud.Gold : new Color(1f, 1f, 1f, 0.25f)) : GearCatalog.Color(p.Rarity));
            string name = p.IsItem ? (trophy ? $"<color={Hud.Hex(Hud.Gold)}>[{GearCatalog.RarityName(Rarity.Legendary)}]</color> " : "") + NameOf(p) : Modding ? GearCatalog.Name(p.Gear.Value) + (v.ModTotal(Loot.MyKey, p.Gear.Value) is int mt && mt > 0 ? $"<color={Hud.Hex(Hud.Gold)}> +{mt}</color>" : "")
                        : v.LabelOf(p.Gear.Value, p.Rarity, Loot.MyKey) + (showStars ? StarSuffix(selectedStar) : "");
            var n = Text(c, name, 26f, new Vector2(RightWidth - 180f, 36f), new Vector2(70f, 142f), TextAlignmentOptions.Left);
            n.fontStyle = FontStyles.Bold;
            n.enableAutoSizing = true; n.fontSizeMin = 16f; n.fontSizeMax = 26f;
            string kind = p.IsItem ? (trophy ? L10n.T("vaultTrophyKind") : L10n.T(Shop ? "smithShopItemKind" : "vaultItemKind"))
                : L10n.T(new[] { "vaultKindFortress", "vaultKindPersonal", "vaultKindActive", "vaultKindWeapon" }[KindOf(p.Gear.Value)]);
            var k = Text(c, kind + (equipped ? $"  <color={Hud.Hex(Hud.Good)}>{L10n.T("vaultInUse")}</color>" : $"  <color={Hud.Hex(Hud.Muted)}>{L10n.T("vaultOwned", Total(v, p))}</color>"),
                         16f, new Vector2(RightWidth - 180f, 50f), new Vector2(70f, 98f), TextAlignmentOptions.TopLeft);
            k.color = Hud.Muted;
            k.enableAutoSizing = true; k.fontSizeMin = 11f; k.fontSizeMax = 16f;
            var lines = new List<string>();
            if (!p.IsItem)
            {
                var g = p.Gear.Value;
                var r = p.Rarity;
                if (Modding) lines.Add($"<color={Hud.Hex(Hud.Gold)}>{L10n.T("modAllRarities")}</color>"); // not common's numbers: the mods are on every rarity
                else lines.Add($"<size=110%>{EffectOf(v, g, r, selectedStar)}</size>");
                if (GearCatalog.IsWeapon(g) && WeaponMods.Summary(v, Loot.MyKey, g) is string mods && mods.Length > 0) lines.Add($"<color={Hud.Hex(Hud.Gold)}>{L10n.T("modCard", mods)}</color>");
                if (r < Rarity.Legendary && !Modding) lines.Add($"<color=#{GearCatalog.Hex(r + 1)}>{L10n.T("vaultNextRarity", v.EffectFor(Loot.MyKey, g, r + 1))}</color>");
                // The stars: which substack the card is showing, all four counts, and what three of them forge into.
                if (showStars)
                {
                    var sc = StarCountsOf(v, g, r);
                    lines.Add($"<color={Hud.Hex(Hud.Gold)}>{StarMark(selectedStar)}</color>  <color={Hud.Hex(Hud.Muted)}>{sc[0]}×0★  {sc[1]}×1★  {sc[2]}×2★  {sc[3]}×3★</color>");
                    bool maxed = selectedStar >= GearCatalog.MaxStars && r >= Rarity.Legendary;
                    string chain = MergeChain(r, selectedStar);
                    if (!maxed) chain += $"  <color={Hud.Hex(Hud.Muted)}>{sc[selectedStar]}/3</color>";
                    lines.Add($"<color={Hud.Hex(!maxed && sc[selectedStar] >= 3 ? Hud.Good : Hud.Muted)}>{chain}</color>");
                }
                if (!GearCatalog.IsActive(g))
                {
                    bool legend = r == Rarity.Legendary;
                    lines.Add($"<color=#{GearCatalog.Hex(Rarity.Legendary)}>{(legend ? "★" : "☆")} {L10n.T("legendTitle")}</color>" + (legend ? "" : $"<size=85%><color=#888888>　{L10n.T("legendOnly")}</color></size>"));
                    lines.Add(legend ? L10n.T("gearLegend" + g) : $"<color=#999999>{L10n.T("gearLegend" + g)}</color>");
                }
            }
            else lines.Add(L10n.T(Shop ? "smithShopExplosive" : "vaultItemTake"));
            var body = Text(c, string.Join("\n", lines), 19f, new Vector2(RightWidth - 50f, 150f), new Vector2(0f, -15f), TextAlignmentOptions.TopLeft);
            body.enableAutoSizing = true; body.fontSizeMin = 13f; body.fontSizeMax = 19f;

            // The card's buttons (the quartermaster's; the bench has its own).
            if (Smith || equipped && !_sel.HasValue) return;
            if (p.IsItem || GearCatalog.IsWeapon(p.Gear.Value))
            {
                var p1 = p;
                var b1 = Btn(c, new Vector2(280f, 46f), new Vector2(-150f, -138f), new Color(0.22f, 0.5f, 0.35f), () => Quick(p1, 1), out var t1);
                t1.text = L10n.T("vaultTake1");
                var b10 = Btn(c, new Vector2(280f, 46f), new Vector2(150f, -138f), new Color(0.2f, 0.36f, 0.3f), () => Quick(p1, 10), out var t10);
                t10.text = L10n.T("vaultTake10");
                b1.interactable = b10.interactable = Total(v, p) > 0;
            }
            else
            {
                var g = p.Gear.Value;
                var r = p.Rarity;
                var target = TargetFor(v, g, r);
                string why = target == null ? L10n.T("vaultFullHint") : Total(v, p) <= 0 ? L10n.T("vaultNoneLeft") : null;
                var b = Btn(c, new Vector2(RightWidth - 60f, 48f), new Vector2(0f, -138f), new Color(0.22f, 0.58f, 0.38f), () => { if (target is SlotRef t) EquipInto(t, g, r, selectedStar); }, out var bt);
                b.interactable = why == null;
                bt.text = why ?? (target is SlotRef ft && ft.Item is GearKey ok ? L10n.T("vaultSwapBtn", GearCatalog.Label(ok.Item1, ok.Item2) + StarSuffix(ok.Stars)) : L10n.T("vaultEquipBtn"));
            }
        }

        // ---------- tiny UI kit ----------

        private static VaultTile Hover(GameObject go, Action enter, Action exit, Action left = null, Action right = null, Action dbl = null)
        {
            var h = go.AddComponent<VaultTile>();
            h.Enter = enter; h.Exit = exit; h.Left = left; h.Right = right; h.Double = dbl;
            return h;
        }

        private static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        // Hidden before Destroy: Destroyed objects live until the end of the frame, and a click in that frame could still land
        // on the old button (the "buttons feel odd" report).
        private static void Clear(Transform t) { foreach (Transform c in t) { c.gameObject.SetActive(false); Destroy(c.gameObject); } }

        private static Transform Node(Transform parent, string name)
        {
            var t = new GameObject(name, typeof(RectTransform)).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static Image Box(Transform parent, Vector2 size, Vector2 pos, Color c, float radius = 12f)
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.rectTransform.sizeDelta = size;
            img.rectTransform.anchoredPosition = pos;
            img.raycastTarget = false;
            Hud.Round(img, radius);
            return img;
        }

        // Rarity ring around a tile.
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
            go.AddComponent<Hud.OneLine>(); // a one-line box never wraps (Hud.OneLine)
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
            label = Text(img.transform, "", 22f, size - new Vector2(12f, 6f), Vector2.zero, TextAlignmentOptions.Center);
            label.enableWordWrapping = false;
            label.enableAutoSizing = true; label.fontSizeMin = 12f; label.fontSizeMax = 22f;
            return b;
        }
    }

    // Mouse over / left / right / double click on a vault tile; dragging one (gear onto the loadout, or off it).
    internal class VaultTile : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action Enter, Exit, Left, Right, Double, BeginDrag, EndDrag;
        public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData e) => Exit?.Invoke();
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) Right?.Invoke();
            else if (e.button == PointerEventData.InputButton.Left) { if (e.clickCount >= 2 && Double != null) Double(); else Left?.Invoke(); }
        }
        public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) BeginDrag?.Invoke(); }
        public void OnDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) => EndDrag?.Invoke();
    }

    // Where a dragged tile can be let go.
    internal class VaultDrop : MonoBehaviour, IDropHandler
    {
        public Action Dropped;
        public void OnDrop(PointerEventData e) => Dropped?.Invoke();
    }

    // While the vault (or any mouse menu, the I help panel too: Hud.ModalOpen) is open: no moving / looking / using items, the
    // mouse stays free, and Esc closes it instead of opening the pause menu.
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
            if (Reveal.Playing) { Reveal.Instance.Skip(); return false; }
            if (FortressIsland.Instance && FortressIsland.Instance.ResultsShowing && !Hud.ModalOpen) { FortressIsland.Instance.CloseResults(); return false; } // Esc closes the results first
            if (!Hud.ModalOpen) return true;
            if (Tips.HelpShown) Tips.Instance.CloseHelp();
            if (VaultUI.IsOpen) VaultUI.Instance.Close();
            if (PieceMenu.IsOpen) PieceMenu.Instance.Close();
            if (StallMenu.IsOpen) StallMenu.Instance.Close();
            return false;
        }
    }
}
