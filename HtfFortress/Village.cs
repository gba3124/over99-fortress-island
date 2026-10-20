using System;
using System.Linq;
using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HtfFortress
{
    // The fish stall's counter (talk to the fishmonger): sell what's in your net, buy a bigger net, buy a grilled fish
    // (eaten on the spot), and the helpers. All paid in the crew's money, round or not (Owen: "隨時都能購"). Mouse-driven;
    // Esc, right-click or walking away closes it.
    public class StallMenu : MonoBehaviour
    {
        public static StallMenu Instance;
        public static bool IsOpen { get; private set; }
        private Canvas _canvas;
        private RectTransform _panel;
        private string _drawn;

        private void Awake() => Instance = this;

        public void Open()
        {
            if (!Hud.Font) return;
            IsOpen = true;
            _drawn = null;
            Hud.Canvas("FortressStall", 610, ref _canvas).gameObject.SetActive(true);
            PlayerCamera.ToggleMouse(true);
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

        private void Update()
        {
            if (!IsOpen) return;
            var mouse = Mouse.current;
            if (!Near() || (mouse != null && mouse.rightButton.wasPressedThisFrame)) { Close(); return; }
            var net = FortressNet.Instance.ClientNet;
            var hv = Loot.Instance.ClientVault;
            string key = $"{net.count}|{net.capacity}|{net.value}|{Siege.Instance.ViewCoins}|{Siege.Instance.ViewActive}|{hv.Helpers}|{hv.HelperSpeed}|{hv.HelperLoad}|{hv.Oven}";
            if (key != _drawn) { _drawn = key; Redraw(); }
        }

        private void Redraw()
        {
            if (_panel) Destroy(_panel.gameObject);
            Vector2 C = new Vector2(0.5f, 0.5f), T = new Vector2(0.5f, 1f), L = new Vector2(0f, 1f);
            var dim = Hud.Box(_canvas.transform, C, Vector2.zero, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.35f), 0f);
            dim.raycastTarget = true;
            _panel = dim.rectTransform;
            // Two columns: the fish (sell, the net, food) on the left, the helpers on the right.
            var p = Hud.Box(_panel, C, new Vector2(0f, 30f), new Vector2(1500f, 720f), Hud.PanelSolid, 24f).transform;
            Hud.Label(p, L10n.T("stallTitle"), 38f, T, new Vector2(0f, -22f), new Vector2(1400f, 48f), TextAlignmentOptions.Center, bold: true);
            Hud.Box(p, T, new Vector2(0f, -76f), new Vector2(90f, 5f), Hud.Accent, 2.5f);
            Hud.Label(p, "“" + L10n.T("stallLine") + "”", 21f, T, new Vector2(0f, -92f), new Vector2(1400f, 30f), TextAlignmentOptions.Center, Hud.Muted);
            const float Left = -370f, Right = 370f;
            Hud.Label(p, L10n.T("stallFishHead"), 23f, T, new Vector2(Left, -128f), new Vector2(700f, 30f), TextAlignmentOptions.Left, Hud.Accent, bold: true);
            Hud.Label(p, L10n.T("stallHelpersHead"), 23f, T, new Vector2(Right, -128f), new Vector2(700f, 30f), TextAlignmentOptions.Left, Hud.Accent, bold: true);

            bool inRound = Siege.Instance && Siege.Instance.ViewActive;
            var net = FortressNet.Instance.ClientNet;
            var vault = Loot.Instance.ClientVault;
            var key = Loot.KeyOf(Player.LocalPlayer);
            int worth = Mathf.RoundToInt(net.value * (1f + vault.PersonalValue(key, Gear.FishGloves)));
            int bonus = Mathf.RoundToInt(vault.PersonalValue(key, Gear.BigNet));
            int lvl = Array.IndexOf(FortressNet.Capacity, net.capacity - bonus);
            bool canGrow = lvl >= 0 && lvl < FortressNet.UpgradeCost.Length;
            int cost = canGrow ? Siege.Money(FortressNet.UpgradeCost[lvl]) : 0;

            // A row each: a picture, what it is, the button.
            void Row(float x, float y, string picture, string title, string detail, string button, bool enabled, Color colour, Action click)
            {
                var row = Hud.Box(p, T, new Vector2(x, y), new Vector2(700f, 110f), Hud.Raised, 16f, pivot: T).transform;
                Hud.Picture(row, picture, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(78f, 78f), new Vector2(0f, 0.5f));
                Hud.Label(row, title, 25f, L, new Vector2(112f, -16f), new Vector2(380f, 36f), TextAlignmentOptions.TopLeft, bold: true, pivot: L);
                Hud.Label(row, detail, 18f, L, new Vector2(112f, -56f), new Vector2(380f, 44f), TextAlignmentOptions.TopLeft, Hud.Muted, pivot: L);
                Btn(row, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(180f, 60f), colour, button, enabled, click); // its right edge: clear of the text
            }
            Row(Left, -166f, "GearFishGloves", L10n.T("stallSellTitle", net.count),
                net.count > 0 ? L10n.T("stallSellDetail", worth) : Hud.Keys(L10n.T("stallSellNone")),
                L10n.T("stallSellBtn", worth), net.count > 0, new Color(0.22f, 0.58f, 0.38f),
                () => { InstanceFinder.ClientManager.Broadcast(new NetRequest { Sell = true }); AutoTest.Count("stall_sell"); });
            Row(Left, -288f, "GearBigNet", canGrow ? L10n.T("stallNetTitle", FortressNet.Capacity[lvl + 1]) : L10n.T("netMax"),
                canGrow ? L10n.T(inRound ? "stallNetDetail" : "stallNetDetailNext", net.capacity) : L10n.T("stallNetMaxDetail"),
                canGrow ? "$" + cost : "—", canGrow && Siege.Instance.ViewCoins >= cost, new Color(0.26f, 0.42f, 0.78f),
                () => InstanceFinder.ClientManager.Broadcast(new NetRequest { Sell = false, Level = lvl }));
            int food = Siege.Money(FortressNet.FoodCost);
            Row(Left, -410f, "StallFood", L10n.T("stallFoodTitle"), L10n.T("stallFoodDetail"),
                "$" + food, Siege.Instance.ViewCoins >= food, new Color(0.78f, 0.45f, 0.18f),
                () => { InstanceFinder.ClientManager.Broadcast(new NetRequest { Food = true }); AutoTest.Count("stall_buy_food"); Close(); }); // closed, to see yourself eat it

            // The helpers: hire, faster, bigger baskets, the oven. Each says what it is now and what the next step gives.
            void HelperRow(float y, byte what, string picture, string title, string detail)
            {
                int price = Helpers.PriceOf(what, vault);
                int dollars = price >= 0 ? Siege.Money(price) : 0;
                Row(Right, y, picture, title, detail, price < 0 ? L10n.T(what == 3 ? "helperOwned" : "helperMaxed") : "$" + dollars,
                    price >= 0 && Siege.Instance.ViewCoins >= dollars, new Color(0.26f, 0.55f, 0.5f),
                    () => { InstanceFinder.ClientManager.Broadcast(new HelperBuy { What = what }); AutoTest.Count("stall_helper_" + what); });
            }
            HelperRow(-166f, 0, "StallHelper", L10n.T("helperHireTitle", vault.Helpers, Helpers.MaxHelpers), L10n.T("helperHireDetail"));
            bool fastest = vault.HelperSpeed >= Helpers.MaxLevel, biggest = vault.HelperLoad >= Helpers.MaxLevel;
            HelperRow(-288f, 1, "StallSpeed", L10n.T("helperSpeedTitle", vault.HelperSpeed, Helpers.MaxLevel),
                fastest ? L10n.T("helperSpeedNow", Helpers.Speed(vault.HelperSpeed).ToString("0.#"))
                        : L10n.T("helperSpeedDetail", Helpers.Speed(vault.HelperSpeed).ToString("0.#"), Helpers.Speed(vault.HelperSpeed + 1).ToString("0.#")));
            HelperRow(-410f, 2, "StallBasket", L10n.T("helperLoadTitle", vault.HelperLoad, Helpers.MaxLevel),
                biggest ? L10n.T("helperLoadNow", Helpers.Basket(vault.HelperLoad)) : L10n.T("helperLoadDetail", Helpers.Basket(vault.HelperLoad), Helpers.Basket(vault.HelperLoad + 1)));
            HelperRow(-532f, 3, "StallOven", L10n.T(vault.Oven ? "helperOvenOwned" : "helperOvenTitle"), L10n.T("helperOvenDetail", Mathf.RoundToInt(Helpers.OvenBonus * 100f)));
            Hud.Label(p, Hud.Keys(L10n.T("stallHint")), 18f, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(1400f, 30f), TextAlignmentOptions.Center, Hud.Muted);
        }

        private static void Btn(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c, string text, bool enabled, Action onClick)
        {
            var img = Hud.Box(parent, anchor, pos, size, c, 14f);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            b.colors = colors;
            b.interactable = enabled;
            b.onClick.AddListener(() => { Fx.Sound("Click", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.6f, AudioDistance.Short); onClick(); });
            var t = Hud.Label(img.transform, text, 22f, new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(12f, 6f), TextAlignmentOptions.Center, bold: true);
            t.enableAutoSizing = true; t.fontSizeMin = 13f; t.fontSizeMax = 22f;
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
