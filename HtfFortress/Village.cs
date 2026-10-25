using System;
using System.Linq;
using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HtfFortress
{
    // The fish stall's counter (talk to the fishmonger): buy a grilled fish
    // (eaten on the spot), and the helpers. All paid in the crew's money, round or not (Owen: "隨時都能購"). Mouse-driven;
    // Esc, right-click or walking away closes it.
    public class StallMenu : MonoBehaviour
    {
        public static StallMenu Instance;
        public static bool IsOpen { get; private set; }
        private Canvas _canvas;
        private Transform _panel;
        private float _nextCheck, _nextRefresh;
        private bool _dirty = true;

        private TextMeshProUGUI _sellTitle, _sellDetail, _sellBtnText;
        private Button _sellBtn;
        private TextMeshProUGUI _netTitle, _netDetail, _netBtnText;
        private Button _netBtn;
        private TextMeshProUGUI _foodBtnText, _backupTitle, _backupDetail, _backupBtnText;
        private Button _foodBtn, _backupBtn;
        private TextMeshProUGUI[] _helpTitle = new TextMeshProUGUI[5], _helpDetail = new TextMeshProUGUI[5], _helpBtnText = new TextMeshProUGUI[5];
        private Button[] _helpBtn = new Button[5];
        private int _netLevel, _netCount, _netValue, _coins;
        private bool _inRound;
        private float _buyLockUntil;

        private void Awake() => Instance = this;

        public void Open()
        {
            if (!Hud.Font) return;
            if (VaultUI.IsOpen && VaultUI.Instance) VaultUI.Instance.Close();
            if (KeeperMenu.IsOpen && KeeperMenu.Instance) KeeperMenu.Instance.Close();
            if (PieceMenu.IsOpen && PieceMenu.Instance) PieceMenu.Instance.Close();
            IsOpen = true;
            Hud.Canvas("FortressStall", 610, ref _canvas).gameObject.SetActive(true);
            PlayerCamera.ToggleMouse(true);
            if (!_panel) Build();
            _dirty = true;
            Refresh();
            AutoTest.Count("stall_opened");
        }

        public void Close()
        {
            IsOpen = false;
            if (_canvas) _canvas.gameObject.SetActive(false);
            PlayerCamera.ToggleMouse(false);
        }

        private static bool Near()
        {
            var island = FortressIsland.Instance;
            return island && island.Exists && Player.LocalPlayer && Player.LocalPlayer.Transform
                   && (Player.LocalPlayer.Transform.position - island.MarketPos).sqrMagnitude < 5f * 5f;
        }

        public static void RefreshSoon()
        {
            if (!Instance) return;
            Instance._dirty = true;
            Instance._nextCheck = 0f;
            Instance._nextRefresh = 0f;
        }

        private void Update()
        {
            if (!IsOpen) return;
            var mouse = Mouse.current;
            if (!Near() || (mouse != null && mouse.rightButton.wasPressedThisFrame)) { Close(); return; }
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.2f;
            if (!_dirty || Time.unscaledTime < _nextRefresh) return;
            _dirty = false;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        // Build once: every row is a fixed widget, Refresh only changes its words (no Destroy/rebuild while browsing).
        private void Build()
        {
            var dim = Hud.Box(_canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.4f), 0f);
            dim.raycastTarget = true;
            _panel = Hud.Box(dim.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1500f, 820f), Hud.PanelSolid, 26f).transform;
            var T = new Vector2(0.5f, 1f);
            Hud.Label(_panel, L10n.T("stallTitle"), 38f, T, new Vector2(0f, -22f), new Vector2(1400f, 48f), TextAlignmentOptions.Center, bold: true);
            Hud.Box(_panel, T, new Vector2(0f, -74f), new Vector2(90f, 5f), Hud.Accent, 2.5f);
            Hud.Label(_panel, "“" + L10n.T("stallLine") + "”", 21f, T, new Vector2(0f, -90f), new Vector2(1400f, 30f), TextAlignmentOptions.Center, Hud.Muted);
            const float LX = 30f, RX = 770f;
            Hud.Label(_panel, L10n.T("stallFishHead"), 23f, new Vector2(0f, 1f), new Vector2(LX + 6f, -126f), new Vector2(650f, 30f), TextAlignmentOptions.Left, Hud.Accent, bold: true);
            Hud.Label(_panel, L10n.T("stallHelpersHeadShort"), 23f, new Vector2(0f, 1f), new Vector2(RX + 6f, -126f), new Vector2(650f, 30f), TextAlignmentOptions.Left, Hud.Accent, bold: true);

            void Row(float x, float y, string picture, out TextMeshProUGUI title, out TextMeshProUGUI detail, out Button button, out TextMeshProUGUI buttonText)
            {
                var row = Hud.Box(_panel, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(700f, 116f), Hud.Raised, 16f).transform;
                Hud.Picture(row, picture, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(74f, 74f), new Vector2(0f, 0.5f));
                title = Hud.Label(row, "", 24f, new Vector2(0f, 1f), new Vector2(112f, -14f), new Vector2(390f, 34f), TextAlignmentOptions.TopLeft, bold: true);
                detail = Hud.Label(row, "", 18f, new Vector2(0f, 1f), new Vector2(112f, -52f), new Vector2(390f, 58f), TextAlignmentOptions.TopLeft, Hud.Muted);
                var img = Hud.Box(row, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(178f, 58f), new Color(0.26f, 0.42f, 0.78f), 14f);
                img.raycastTarget = true;
                button = img.gameObject.AddComponent<Button>();
                var colors = button.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f); colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f); button.colors = colors;
                buttonText = Hud.Label(img.transform, "", 21f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(166f, 50f), TextAlignmentOptions.Center, bold: true);
                buttonText.enableAutoSizing = true; buttonText.fontSizeMin = 13f; buttonText.fontSizeMax = 21f;
            }
            Row(LX, -164f, "StallFood", out var foodTitle, out var foodDetail, out _foodBtn, out _foodBtnText);
            foodTitle.text = L10n.T("stallFoodTitle"); foodDetail.text = L10n.T("stallFoodDetail");
            Row(LX, -288f, "StallFood", out _backupTitle, out _backupDetail, out _backupBtn, out _backupBtnText);
            string[] pics = { "StallHelper", "StallSpeed", "StallBasket", "StallOven", "StallHelper" };
            for (int i = 0; i < 5; i++) Row(RX, -164f - 126f * i, pics[i], out _helpTitle[i], out _helpDetail[i], out _helpBtn[i], out _helpBtnText[i]);

            _foodBtn.onClick.AddListener(() => { Send(new NetRequest { Food = true }, "stall_buy_food"); Close(); });
            _backupBtn.onClick.AddListener(() =>
            {
                if (Time.unscaledTime < _buyLockUntil) return;
                _buyLockUntil = Time.unscaledTime + 0.08f;
                FortressNet.Instance.PreviewBackup(FortressNet.BackupPack);
                Send(new NetRequest { Food = true, Level = FortressNet.BackupPack }, "stall_buy_backup");
                Refresh();
            });
            for (int i = 0; i < 5; i++)
            {
                int what = i == 4 ? 4 : i;
                _helpBtn[i].onClick.AddListener(() =>
                {
                    if (Time.unscaledTime < _buyLockUntil) return;
                    _buyLockUntil = Time.unscaledTime + 0.08f;
                    PreviewHelper(what);
                    Send(new HelperBuy { What = (byte)what }, "stall_helper_" + what);
                    Refresh();
                });
            }
            Hud.Label(_panel, Hud.Keys(L10n.T("stallHint")), 18f, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(1400f, 30f), TextAlignmentOptions.Center, Hud.Muted);
        }

        // The host owns these; show the bought step on the same click, then let the vault answer correct it.
        private static void PreviewHelper(int what)
        {
            var v = Loot.Instance ? Loot.Instance.ClientVault : null;
            if (v == null) return;
            switch (what)
            {
                case 0: if (v.Helpers < Helpers.MaxHelpers) v.Helpers++; break;
                case 1: if (v.HelperSpeed < Helpers.MaxLevel) v.HelperSpeed++; break;
                case 2: if (v.HelperLoad < Helpers.MaxLevel) v.HelperLoad++; break;
                case 3: v.Oven = true; break;
                case 4: if (v.Helpers > 0) v.Helpers--; break;
            }
        }

        private static void Send<T>(T msg, string test) where T : struct, FishNet.Broadcast.IBroadcast
        {
            InstanceFinder.ClientManager.Broadcast(msg);
            AutoTest.Count(test);
        }

        private void Refresh()
        {
            if (!_panel) return;
            var net = FortressNet.Instance.ClientNet;
            var vault = Loot.Instance.ClientVault;
            var key = Loot.KeyOf(Player.LocalPlayer);
            _coins = Siege.Instance.ViewCoins;
            _inRound = Siege.Instance.ViewActive;
            _netCount = net.count; _netValue = net.value;
            int worth = Mathf.RoundToInt(net.value * (1f + vault.PersonalValue(key, Gear.FishGloves)));
            _netLevel = Array.IndexOf(FortressNet.Capacity, net.capacity);
            bool canGrow = _netLevel >= 0 && _netLevel < FortressNet.UpgradeCost.Length;
            int netPrice = canGrow ? Siege.Money(FortressNet.UpgradeCost[_netLevel]) : 0;

            Set(_sellTitle, L10n.T("stallSellTitle", net.count));
            Set(_sellDetail, net.count > 0 ? L10n.T("stallSellDetail", worth) : Hud.Keys(L10n.T("stallSellNone")));
            Btn(_sellBtn, _sellBtnText, L10n.T("stallSellBtn", worth), net.count > 0, new Color(0.22f, 0.58f, 0.38f));

            Set(_netTitle, canGrow ? L10n.T("stallNetTitle", FortressNet.Capacity[_netLevel + 1]) : L10n.T("netMax"));
            Set(_netDetail, canGrow ? L10n.T(_inRound ? "stallNetDetail" : "stallNetDetailNext", net.capacity) : L10n.T("stallNetMaxDetail"));
            Btn(_netBtn, _netBtnText, canGrow ? "$" + netPrice : "—", canGrow && _coins >= netPrice, new Color(0.26f, 0.42f, 0.78f));

            int food = Siege.Money(FortressNet.FoodCost);
            Btn(_foodBtn, _foodBtnText, "$" + food, _coins >= food, new Color(0.78f, 0.45f, 0.18f));
            int backup = FortressNet.Instance.ClientBackup, pack = Siege.Money(FortressNet.BackupCost);
            Set(_backupTitle, L10n.T("stallBackupTitle", backup));
            Set(_backupDetail, L10n.T("stallBackupDetail", FortressNet.BackupPack));
            Btn(_backupBtn, _backupBtnText, "$" + pack, _coins >= pack, new Color(0.55f, 0.38f, 0.16f));

            HelperRow(0, L10n.T("helperHireTitleShort", vault.Helpers, Helpers.MaxHelpers), L10n.T("helperHireShort", Helpers.HumanHelpers));
            bool fastest = vault.HelperSpeed >= Helpers.MaxLevel, biggest = vault.HelperLoad >= Helpers.MaxLevel;
            HelperRow(1, L10n.T("helperSpeedTitle", vault.HelperSpeed, Helpers.MaxLevel),
                fastest ? L10n.T("helperSpeedShort", Helpers.Speed(vault.HelperSpeed).ToString("0.#"))
                        : L10n.T("helperSpeedShort", Helpers.Speed(vault.HelperSpeed).ToString("0.#") + " → " + Helpers.Speed(vault.HelperSpeed + 1).ToString("0.#")));
            HelperRow(2, L10n.T("helperLoadTitle", vault.HelperLoad, Helpers.MaxLevel),
                biggest ? L10n.T("helperLoadShort", Helpers.Basket(vault.HelperLoad))
                        : L10n.T("helperLoadShort", Helpers.Basket(vault.HelperLoad) + " → " + Helpers.Basket(vault.HelperLoad + 1)));
            HelperRow(3, L10n.T(vault.Oven ? "helperOvenOwned" : "helperOvenTitle"), L10n.T("helperOvenShort", Mathf.RoundToInt(Helpers.OvenBonus * 100f)));
            HelperRow(4, L10n.T("stallDismissTitle"), L10n.T("helperDismissShort"));
        }

        private void HelperRow(int i, string title, string detail)
        {
            byte what = i == 4 ? (byte)4 : (byte)i;
            int price = i == 4 ? -2 : Helpers.PriceOf(what, Loot.Instance.ClientVault);
            int dollars = price >= 0 ? Siege.Money(price) : 0;
            string label = i == 4 ? L10n.T("stallDismissBtn")
                : price < 0 ? L10n.T(what == 3 ? "helperOwned" : "helperMaxed") : "$" + dollars;
            bool enabled = i == 4 ? Loot.Instance.ClientVault.Helpers > 0 : price >= 0 && _coins >= dollars;
            Set(_helpTitle[i], title); Set(_helpDetail[i], detail);
            Btn(_helpBtn[i], _helpBtnText[i], label, enabled, i == 4 ? new Color(0.55f, 0.32f, 0.30f) : new Color(0.26f, 0.55f, 0.5f));
        }

        private static void Set(TextMeshProUGUI t, string s) { if (t && t.text != s) t.text = s; }
        private static void Btn(Button b, TextMeshProUGUI label, string text, bool on, Color colour)
        {
            if (!b) return;
            b.interactable = on;
            if (label) { if (label.text != text) label.text = text; label.color = on ? Color.white : new Color(0.85f, 0.85f, 0.85f, 0.65f); }
            var img = b.GetComponent<Image>(); if (img) img.color = on ? colour : new Color(colour.r * 0.55f, colour.g * 0.55f, colour.b * 0.55f, 0.8f);
        }
    }

    // The forge's light breathes like coals do.
    internal class Flicker : MonoBehaviour
    {
        private Light _light;
        private float _base, _seed;
        private void Start() { _light = GetComponent<Light>(); _base = _light.intensity; _seed = UnityEngine.Random.value * 100f; }
        private void Update() => _light.intensity = _base * (0.75f + 0.45f * Mathf.PerlinNoise(_seed, Time.time * 3f));
    }

    // The smithy's anvil (host): a fortress weapon (or one of the game's guns) dropped or thrown onto it goes into the vault, for the bench.
    internal class SmithyCounter : MonoBehaviour
    {
        private void OnTriggerStay(Collider other)
        {
            if (!InstanceFinder.IsServerStarted || !Loot.Instance) return;
            var item = ItemManager.Get(other);
            if (!item || item.Holder || item.IsDeinitializing || item.IsDestroying || !WeaponItems.AsFortress(item, out var g, out var r)) return; // a game gun goes on as a common
            Loot.Instance.ServerDepositThrown(item, g, r, transform.TransformPoint(0f, 1.0f, 0.55f));
        }
    }

    // The vault's door (host): anything thrown at it goes in (Loot.ServerStoreThrown says what may).
    internal class VaultChute : MonoBehaviour
    {
        private void OnTriggerStay(Collider other)
        {
            if (!InstanceFinder.IsServerStarted || !Loot.Instance) return;
            var item = ItemManager.Get(other);
            if (Loot.Storable(item)) Loot.Instance.ServerStoreThrown(item, transform.position);
        }
    }

    // The smithy's back wall shows the rack: one of each kind of weapon the team keeps there, in its best colour, with a
    // bar of that colour under it. Slots match RACK_X / RACK_Z in tools/make_station_models.py (Blender x is the game's -x,
    // Blender y the game's -z).
    // The smithy's back wall shows what its shop sells (Owen: the bench reads the vault and your hands, so the rack is for
    // the shop): a common of every weapon on three rails of seven, the game's guns as the game draws them, a price board
    // over them. Slots match RACK_X / RACK_Z in tools/make_station_models.py (Blender x is the game's -x).
    internal class WeaponRack : MonoBehaviour
    {
        private bool _built;
        private static readonly float[] SlotY = { 2.44f, 1.89f, 1.34f };
        private const float SlotZ = -1.22f, Length = 0.3f;
        private static float SlotX(int k) => 0.15f - k * 0.33f;

        private void Update()
        {
            if (_built || !WeaponItems.Ready || !Hud.Font) return;
            _built = true;
            int i = 0;
            foreach (var g in WeaponItems.Kinds.Take(21))
            {
                var slot = new GameObject("Rack" + g).transform;
                slot.SetParent(transform, false);
                slot.localPosition = new Vector3(SlotX(i % 7), SlotY[i / 7], SlotZ);
                slot.localRotation = Quaternion.Euler(0f, 90f, 0f); // the barrel along the wall
                var model = Model(g, slot);
                var mesh = model && model.GetComponent<MeshFilter>() ? model.GetComponent<MeshFilter>().sharedMesh : null;
                if (mesh)
                {
                    var b = mesh.bounds;
                    float k = Length / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                    model.transform.localScale = Vector3.one * k;
                    model.transform.localPosition = -(model.transform.localRotation * b.center) * k + Vector3.up * b.extents.y * k; // resting on the pegs
                }
                i++;
            }
            Sign();
        }

        // Ours, or the game's own gun (its hotbar mesh, in the colours its model is drawn in).
        private static GameObject Model(Gear g, Transform slot)
        {
            if (!GearCatalog.IsGameGun(g)) return Models.Attach(Weapons.ModelName(g), slot, new Vector3(0.1f, 0.1f, Length));
            var item = GameInfo.GetSpawnable(WeaponItems.GameName(g));
            if (!item || !item.Mesh) return null;
            var shown = item.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r is SkinnedMeshRenderer sk ? sk.sharedMesh == item.Mesh : r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh == item.Mesh);
            var go = new GameObject("Gun" + g, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(slot, false);
            var size = item.Mesh.bounds.size; // lie it down: its longest side along the rail, like ours
            go.transform.localRotation = size.x >= size.y && size.x >= size.z ? Quaternion.Euler(0f, 90f, 0f) : size.y >= size.z ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            go.GetComponent<MeshFilter>().sharedMesh = item.Mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = shown ? shown.sharedMaterials : new[] { Models.Make(new Color(0.25f, 0.25f, 0.28f)) };
            return go;
        }

        // "Every one $3,000 (in a round)" on a board over the rails.
        private void Sign()
        {
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(board.GetComponent<Collider>());
            board.transform.SetParent(transform, false);
            board.transform.localPosition = new Vector3(-0.85f, 2.78f, -1.3f);
            board.transform.localScale = new Vector3(1.5f, 0.24f, 0.04f);
            var wood = Models.Make(new Color(0.42f, 0.29f, 0.18f));
            board.GetComponent<Renderer>().sharedMaterial = wood;
            board.AddComponent<Models.OwnMaterials>().Mats.Add(wood);
            var canvasGo = new GameObject("RackPrice", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(720f, 110f);
            rt.localPosition = new Vector3(-0.85f, 2.78f, -1.275f);
            rt.localRotation = Quaternion.Euler(0f, 180f, 0f); // reading toward the customers
            rt.localScale = Vector3.one * 0.002f;
            Hud.Label(rt, L10n.T("rackSign", Siege.Money(Loot.WeaponPrice)), 64f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 110f), TMPro.TextAlignmentOptions.Center, new Color(0.96f, 0.86f, 0.6f), bold: true);
        }
    }

    // The notice board by the start circle: the best run on this save and every OVER 99 (date and team). Painted on a
    // paper pinned to the board, so it reads in the world; redrawn when the vault (which keeps both) changes.
    internal class RecordBoard : MonoBehaviour
    {
        private VaultData _drawn;
        private TMPro.TextMeshProUGUI _text;

        private void Start()
        {
            var wood = Models.Make(new Color(0.45f, 0.31f, 0.18f));
            var dark = Models.Make(new Color(0.3f, 0.2f, 0.12f));
            gameObject.AddComponent<Models.OwnMaterials>().Mats.AddRange(new[] { wood, dark });
            Block(new Vector3(-0.85f, 1.05f, 0f), new Vector3(0.1f, 2.1f, 0.1f), wood);
            Block(new Vector3(0.85f, 1.05f, 0f), new Vector3(0.1f, 2.1f, 0.1f), wood);
            Block(new Vector3(0f, 1.45f, 0f), new Vector3(1.9f, 1.25f, 0.08f), wood);
            Block(new Vector3(0f, 2.16f, -0.05f), new Vector3(2.1f, 0.08f, 0.36f), dark);
            var col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(1.9f, 2.2f, 0.2f); col.center = new Vector3(0f, 1.1f, 0f);

            var canvasGo = new GameObject("RecordBoardText", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(850f, 540f);
            rt.localPosition = new Vector3(0f, 1.45f, -0.045f);
            rt.localScale = Vector3.one * 0.002f;
            // The paper is drawn on the canvas, not lit: a lit cream glowed in the sun and washed the words out.
            Hud.Box(rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(850f, 540f), new Color(0.9f, 0.85f, 0.72f), 6f);
            _text = Hud.Label(rt, "", 40f, new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(800f, 510f), TMPro.TextAlignmentOptions.Top, new Color(0.2f, 0.13f, 0.07f));
            _text.lineSpacing = 4f;
        }

        private void Block(Vector3 at, Vector3 size, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(transform, false);
            b.transform.localPosition = at;
            b.transform.localScale = size;
            b.GetComponent<Renderer>().sharedMaterial = m;
        }

        private void Update()
        {
            var v = Loot.Instance ? Loot.Instance.ClientVault : null;
            if (v == null || v == _drawn || !_text || !Hud.Font) return;
            _drawn = v;
            var s = new System.Text.StringBuilder();
            s.Append($"<size=145%><b>{L10n.T("boardTitle")}</b></size>\n");
            s.Append($"<size=40%> </size>\n<b>{L10n.T("boardBest", v.Best)}</b>\n");
            if (v.Wins.Count == 0) s.Append($"<color=#6b5a46>{L10n.T("boardNone")}</color>");
            else
            {
                s.Append($"<color=#a35a00><b>{L10n.T("boardWins", v.Wins.Count)}</b></color>\n<size=82%>");
                foreach (var (date, team) in v.Wins.Skip(Mathf.Max(0, v.Wins.Count - 4)).Reverse()) s.Append($"★ {date}　{team}\n");
                s.Append("</size>");
            }
            _text.text = s.ToString();
        }
    }
}
