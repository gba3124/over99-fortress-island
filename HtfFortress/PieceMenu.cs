using System;
using System.Collections.Generic;
using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HtfFortress
{
    // Right-click a piece in build mode (or U on a turret about to choose its Lv3 branch, in build mode or walking up to it):
    // what it is, how healthy, what the next level (or each Lv3 branch) does and costs, plus repair and take down.
    // Mouse-driven; the game's input is held while it is open.
    public class PieceMenu : MonoBehaviour
    {
        public static PieceMenu Instance;
        public static bool IsOpen { get; private set; }

        private Canvas _canvas;
        private RectTransform _panel;
        private int _id;
        private string _drawn;
        private bool _confirmRemove;
        private bool _walkUp; // opened by walking up to the piece: no build mode needed, it closes when you walk off

        private void Awake() => Instance = this;

        public void Open(int id, bool walkUp = false)
        {
            _walkUp = walkUp;
            if (!BuildSystem.Instance.TryGetState(id, out _) || !Hud.Font) return;
            _id = id;
            _drawn = null;
            _confirmRemove = false;
            IsOpen = true;
            Hud.Canvas("FortressPieceMenu", 610, ref _canvas).gameObject.SetActive(true);
            PlayerCamera.ToggleMouse(true);
            Fx.Sound("Click", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.6f, AudioDistance.Short);
        }

        public static int ClosedFrame = -10;
        private const float WalkOff = 6f;

        public void Close()
        {
            if (IsOpen) ClosedFrame = Time.frameCount;
            IsOpen = false;
            if (_canvas) _canvas.gameObject.SetActive(false);
            PlayerCamera.ToggleMouse(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            var mouse = Mouse.current;
            bool gone = !BuildSystem.Instance.TryGetState(_id, out var s);
            var me = Player.LocalPlayer;
            bool walkedOff = _walkUp && (!me || !me.Transform || (me.Transform.position - s.Pos).sqrMagnitude > WalkOff * WalkOff);
            // (It closed itself outside build mode, so U on a Lv2 turret while walking about opened nothing: Owen's missing
            // branch choice.)
            if (gone || (!BuildSystem.Building && !_walkUp) || walkedOff || !(Siege.Instance && Siege.Instance.ViewActive)
                || (mouse != null && mouse.rightButton.wasPressedThisFrame)) { Close(); return; }
            string key = $"{s.Level}|{s.Branch}|{s.Hp}|{s.MaxHp}|{Siege.Instance.ViewCoins}|{_confirmRemove}";
            if (key != _drawn) { _drawn = key; Redraw(s); }
        }

        // ---------- layout ----------

        private void Redraw(PieceState s)
        {
            if (_panel) Destroy(_panel.gameObject);
            Vector2 C = new Vector2(0.5f, 0.5f), T = new Vector2(0.5f, 1f);
            bool branching = Catalog.HasBranches(s.Type) && s.Level == Catalog.BranchLevel - 1;
            float h = 150f + (branching ? 330f : 210f) + 90f;
            var dim = Hud.Box(_canvas.transform, C, Vector2.zero, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.35f), 0f);
            dim.raycastTarget = true;
            _panel = dim.rectTransform;
            var panel = Hud.Box(_panel, C, new Vector2(0f, 30f), new Vector2(760f, h), Hud.PanelSolid, 24f);
            var p = panel.transform;
            int coins = Siege.Instance.ViewCoins;

            // Picture, title, stars, health.
            Hud.Picture(p, PictureOf(s), new Vector2(0f, 1f), new Vector2(26f, -18f), new Vector2(80f, 80f), new Vector2(0f, 1f));
            string branch = s.Branch > 0 ? $"  <color={Hud.Hex(Hud.Accent)}>{L10n.T(Catalog.BranchKey(s.Type, s.Branch))}</color>" : "";
            string stars = Catalog.Stars(s.Level);
            Hud.Label(p, $"{BuildSystem.PieceName(s.Type, s.Level)}{branch}   <color={Hud.Hex(Hud.Gold)}>{stars}</color>", 32f, T, new Vector2(0f, -22f), new Vector2(720f, 44f), TextAlignmentOptions.Center, bold: true);
            float f = s.MaxHp > 0 ? (float)s.Hp / s.MaxHp : 1f;
            var bar = Hud.Bar.Make(p, T, new Vector2(-40f, -86f), new Vector2(460f, 16f), Hud.HealthColor(f));
            bar.Set(f);
            Hud.Label(p, $"{s.Hp} / {s.MaxHp}", 22f, T, new Vector2(204f, -77f), new Vector2(160f, 34f), TextAlignmentOptions.Left, Hud.Muted, pivot: new Vector2(0f, 1f));
            float y = -130f;

            // Next level, or the two Lv3 branches side by side (there is always a next level: it just costs more).
            {
                int cost = Perks.MyCost(Catalog.UpgradeCost(s.Type, s.Level));
                if (branching)
                {
                    for (int b = 1; b <= 2; b++)
                    {
                        int pick = b;
                        var card = Hud.Box(p, T, new Vector2((b == 1 ? -1 : 1) * 182f, y), new Vector2(350f, 312f), Hud.Raised, 18f, pivot: T);
                        var c = card.transform;
                        Hud.Label(c, L10n.T(Catalog.BranchKey(s.Type, b)), 28f, T, new Vector2(0f, -14f), new Vector2(330f, 38f), TextAlignmentOptions.Center, Hud.Accent, bold: true);
                        Hud.Label(c, L10n.T(Catalog.BranchKey(s.Type, b) + "_d"), 20f, T, new Vector2(0f, -54f), new Vector2(320f, 52f), TextAlignmentOptions.Center, Hud.Muted);
                        Hud.Label(c, string.Join("\n", Deltas(s, Catalog.BranchLevel, b)), 21f, T, new Vector2(0f, -110f), new Vector2(310f, 120f), TextAlignmentOptions.Top);
                        Btn(c, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(300f, 50f), new Color(0.26f, 0.42f, 0.78f),
                            L10n.T("menuPick", cost), coins >= cost, () => Send(new PieceUpgradeRequest { Id = _id, Branch = pick, FromLevel = s.Level }));
                    }
                    y -= 330f;
                }
                else
                {
                    var card = Hud.Box(p, T, new Vector2(0f, y), new Vector2(716f, 192f), Hud.Raised, 18f, pivot: T);
                    var c = card.transform;
                    string perk = Catalog.IsTurret(s.Type) && Catalog.Milestones(s.Level + 1) > Catalog.Milestones(s.Level)
                        ? $"   <color={Hud.Hex(Hud.Gold)}>{L10n.T("milestone" + s.Type)}</color>" : ""; // Lv5, Lv10, Lv15: something new
                    Hud.Label(c, L10n.T("menuNext", s.Level + 1, BuildSystem.PieceName(s.Type, s.Level + 1)) + perk, 26f, T, new Vector2(0f, -14f), new Vector2(690f, 36f), TextAlignmentOptions.Center, Hud.Accent, bold: true);
                    var line = Hud.Label(c, string.Join("　 ", Deltas(s, s.Level + 1, s.Branch)), 21f, T, new Vector2(0f, -62f), new Vector2(700f, 60f), TextAlignmentOptions.Center);
                    line.enableWordWrapping = false;
                    Btn(c, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(320f, 54f), new Color(0.26f, 0.42f, 0.78f),
                        L10n.T("menuUpgrade", cost), coins >= cost, () => Send(new PieceUpgradeRequest { Id = _id, Branch = 1, FromLevel = s.Level }));
                    y -= 210f;
                }
            }

            // Repair / move / take down / close.
            int repair = s.Hp < s.MaxHp ? Perks.MyCost(Catalog.RepairCost(s.Hp, s.MaxHp)) : 0;
            Btn(p, new Vector2(0.5f, 0f), new Vector2(-270f, 24f), new Vector2(170f, 54f), new Color(0.22f, 0.55f, 0.36f),
                repair > 0 ? L10n.T("menuRepair", repair) : L10n.T("menuHealthy"), repair > 0 && coins >= repair, () => Send(new PieceUpgradeRequest { Id = _id, Repair = true }));
            Btn(p, new Vector2(0.5f, 0f), new Vector2(-90f, 24f), new Vector2(170f, 54f), new Color(0.26f, 0.42f, 0.78f), L10n.T("menuMove"), true, () =>
            {
                int id = _id;
                Close();
                BuildSystem.Instance.StartMove(id);
            });
            Btn(p, new Vector2(0.5f, 0f), new Vector2(90f, 24f), new Vector2(170f, 54f), new Color(0.62f, 0.2f, 0.16f),
                _confirmRemove ? L10n.T("menuRemoveSure") : L10n.T("menuRemove", BuildSystem.RefundEstimate(s)), true, () =>
                {
                    if (!_confirmRemove) { _confirmRemove = true; return; }
                    InstanceFinder.ClientManager.Broadcast(new PieceRemoveRequest { Id = _id });
                    Close();
                });
            Btn(p, new Vector2(0.5f, 0f), new Vector2(270f, 24f), new Vector2(170f, 54f), new Color(0.3f, 0.33f, 0.38f), L10n.T("menuClose"), true, Close);
        }

        // The piece as it stands: a wall shows its current material.
        public static string PictureOf(PieceState s) =>
            s.Type == PieceType.Wall ? (s.Level >= 3 ? "PieceWallMetal" : s.Level == 2 ? "PieceWallStone" : "PieceWall") : "Piece" + s.Type;

        private void Send(PieceUpgradeRequest r)
        {
            if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(r);
            Fx.Sound("Click", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.6f, AudioDistance.Short);
        }

        // "Damage 6 → 9", "Range 16 → 18", "Fire rate 4/s → 5/s" (turrets); "Health 200 → 400" (everything else).
        internal static List<string> Deltas(PieceState s, int toLevel, int branch)
        {
            string Arrow(string label, string a, string b) => a == b ? $"{label} {a}" : $"{label} {a} → <color={Hud.Hex(Hud.Good)}><b>{b}</b></color>";
            var lines = new List<string>();
            if (s.Type == PieceType.Bubble)
            {
                var now = Catalog.Turret(s.Type, s.Level, s.Branch);
                var next = Catalog.Turret(s.Type, toLevel, branch);
                lines.Add(Arrow(L10n.T("statShield"), Mathf.RoundToInt(now.Shield * 100f) + "%", Mathf.RoundToInt(next.Shield * 100f) + "%"));
                lines.Add(Arrow(L10n.T("statRadius"), now.Range.ToString("0"), next.Range.ToString("0")));
            }
            else if (s.Type == PieceType.Mender)
            {
                var now = Catalog.Turret(s.Type, s.Level, s.Branch);
                var next = Catalog.Turret(s.Type, toLevel, branch);
                lines.Add(Arrow(L10n.T("statHeal"), now.Damage + "/s", next.Damage + "/s"));
                lines.Add(Arrow(L10n.T("statRadius"), now.Range.ToString("0"), next.Range.ToString("0")));
            }
            else if (Catalog.IsTurret(s.Type))
            {
                var now = Catalog.Turret(s.Type, s.Level, s.Branch);
                var next = Catalog.Turret(s.Type, toLevel, branch);
                if (s.Type == PieceType.Flame) lines.Add(Arrow(L10n.T("statBurn"), now.Burn.ToString("0") + "/s", next.Burn.ToString("0") + "/s"));
                string Dmg(Catalog.TurretStats t) => t.Shots * t.Targets > 1 ? $"{t.Damage}×{t.Shots * t.Targets}" : t.Damage.ToString(); // ×shells or ×fish at once
                lines.Add(Arrow(L10n.T("statDamage"), Dmg(now), Dmg(next)));
                if (s.Type == PieceType.Tesla) lines.Add(Arrow(L10n.T("statChain"), now.Chain.ToString(), next.Chain.ToString()));
                lines.Add(Arrow(L10n.T("statRate"), (1f / now.Interval).ToString("0.#") + "/s", (1f / next.Interval).ToString("0.#") + "/s"));
                lines.Add(Arrow(L10n.T("statRange"), now.Range.ToString("0"), next.Range.ToString("0")));
            }
            // Health scales with the same gear bonus the piece already has.
            float ratio = Catalog.MaxHp(s.Type, s.Level) > 0 ? (float)s.MaxHp / Catalog.MaxHp(s.Type, s.Level) : 1f;
            lines.Add(Arrow(L10n.T("statHp"), s.MaxHp.ToString(), Mathf.RoundToInt(Catalog.MaxHp(s.Type, toLevel) * ratio).ToString()));
            if (s.Type == PieceType.Spikes) lines.Add(Arrow(L10n.T("statDamage"), Catalog.SpikeDamage(s.Level).ToString(), Catalog.SpikeDamage(toLevel).ToString()));
            return lines;
        }

        private static void Btn(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c, string text, bool enabled, Action onClick)
        {
            var img = Hud.Box(parent, anchor, pos, size, c, 14f, pivot: new Vector2(0.5f, anchor.y));
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            b.colors = colors;
            b.interactable = enabled;
            b.onClick.AddListener(() => onClick());
            Hud.Label(img.transform, text, 22f, new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(12f, 6f), TextAlignmentOptions.Center, bold: true);
        }

        // Auto-test hooks.
        public void DebugUpgrade(int branch) => Send(new PieceUpgradeRequest { Id = _id, Branch = branch, FromLevel = BuildSystem.Instance.LevelOf(_id) });
    }
}
