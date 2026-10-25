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
        private int _pendingLevel, _pendingBranch; private float _pendingUntil; // show the bought level at once, before the host's state arrives

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
            if (_pendingLevel > 0 && Time.unscaledTime >= _pendingUntil) { _pendingLevel = 0; _drawn = null; } // host rejected or was slow: back to the real state
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
            if (_pendingLevel > s.Level && Time.unscaledTime < _pendingUntil)
            {
                s.Level = _pendingLevel; s.Branch = _pendingBranch;
                s.MaxHp = Catalog.MaxHp(s.Type, s.Level); s.Hp = Mathf.Min(s.Hp, s.MaxHp);
            }
            if (_panel) Destroy(_panel.gameObject);
            Vector2 C = new Vector2(0.5f, 0.5f), T = new Vector2(0.5f, 1f);
            bool branching = Catalog.HasBranches(s.Type) && s.Level == Catalog.BranchLevel - 1;
            bool freeSplit = Catalog.HasSecondBranch(s.Type) && s.Level >= Catalog.SecondBranchLevel && s.Branch <= 2;
            bool second = Catalog.HasSecondBranch(s.Type) && (s.Level == Catalog.SecondBranchLevel - 1 || freeSplit);
            bool choosing = branching || second;
            float h = choosing ? 770f : 630f;
            var dim = Hud.Box(_canvas.transform, C, Vector2.zero, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.42f), 0f);
            dim.raycastTarget = true;
            _panel = dim.rectTransform;
            var panel = Hud.Box(_panel, C, new Vector2(0f, 20f), new Vector2(880f, h), Hud.PanelSolid, 26f);
            var p = panel.transform;
            int coins = Siege.Instance.ViewCoins;

            // Header: icon, name and branch, stars; a full-width health bar under it.
            Hud.Picture(p, PictureOf(s), new Vector2(0f, 1f), new Vector2(30f, -22f), new Vector2(76f, 76f), new Vector2(0f, 1f));
            string branch = s.Branch > 0 ? "  <color=" + Hud.Hex(Hud.Accent) + ">" + Catalog.BranchName(s.Type, s.Branch) + "</color>" : "";
            var titleLabel = Hud.Label(p, BuildSystem.PieceName(s.Type, s.Level) + branch, 33f, new Vector2(0f, 1f), new Vector2(122f, -20f), new Vector2(700f, 42f), TextAlignmentOptions.Left, bold: true);
            titleLabel.enableAutoSizing = true; titleLabel.fontSizeMin = 24f; titleLabel.fontSizeMax = 33f;
            Hud.Label(p, Catalog.Stars(s.Level), 24f, new Vector2(0f, 1f), new Vector2(122f, -58f), new Vector2(700f, 30f), TextAlignmentOptions.Left, Hud.Gold);
            float hp = s.MaxHp > 0 ? (float)s.Hp / s.MaxHp : 1f;
            // Leave the right-hand number its own column: a full-width bar running under the "600 / 600" text was the
            // turret panel's layout break.
            var bar = Hud.Bar.Make(p, T, new Vector2(-118f, -104f), new Vector2(610f, 16f), Hud.HealthColor(hp));
            bar.Set(hp);
            Hud.Label(p, s.Hp + " / " + s.MaxHp, 19f, new Vector2(1f, 1f), new Vector2(-44f, -104f), new Vector2(220f, 24f), TextAlignmentOptions.Right, Hud.Muted);

            // Current numbers in four equal tiles.
            var stats = StatsOf(s);
            for (int i = 0; i < stats.Count && i < 4; i++)
            {
                float x = -333f + i * 222f;
                var box = Hud.Box(p, T, new Vector2(x, -144f), new Vector2(204f, 64f), Hud.Raised, 12f, pivot: T);
                Hud.Label(box.transform, stats[i].Item1, 16f, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(190f, 22f), TextAlignmentOptions.Center, Hud.Muted);
                Hud.Label(box.transform, stats[i].Item2, 24f, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(190f, 30f), TextAlignmentOptions.Center, Hud.Ink, bold: true);
            }

            float y = -224f;
            int cost = Perks.MyTowerUpgradeCost(Catalog.UpgradeCost(s.Type, s.Level));
            if (choosing)
            {
                int firstPick = s.Branch > 0 ? Catalog.FirstOf(s.Branch) : 1;
                Hud.Label(p, L10n.T(second ? "menuChooseSecond" : "menuChooseFirst"), 21f, T, new Vector2(0f, y), new Vector2(800f, 28f), TextAlignmentOptions.Center, Hud.Accent, bold: true);
                y -= 36f;
                for (int k = 0; k < 2; k++)
                {
                    int code = second ? Catalog.BranchCode(firstPick, k + 1) : k + 1;
                    if (k == 0) BuildSystem.Log.LogInfo($"piece menu {s.Type} Lv{s.Level} branch {s.Branch}: split options {code}/{Catalog.BranchCode(firstPick, 2)} ({Catalog.BranchName(s.Type, code)} / {Catalog.BranchName(s.Type, second ? Catalog.BranchCode(firstPick, 2) : 2)})");
                    var card = Hud.Box(p, T, new Vector2((k == 0 ? -1 : 1) * 198f, y), new Vector2(380f, 390f), Hud.Raised, 18f, pivot: T);
                    var c = card.transform;
                    if (second)
                    {
                        string tag = (Catalog.FirstOf(code) == 1 ? "A" : "B") + Catalog.SecondOf(code);
                        Hud.Label(c, tag, 20f, new Vector2(0f, 1f), new Vector2(18f, -16f), new Vector2(46f, 28f), TextAlignmentOptions.Left, Hud.Gold, bold: true);
                    }
                    Hud.Label(c, Catalog.BranchName(s.Type, code), 26f, T, new Vector2(0f, -16f), new Vector2(360f, 36f), TextAlignmentOptions.Center, Hud.Accent, bold: true);
                    Hud.Label(c, Catalog.BranchDesc(s.Type, code), 18f, T, new Vector2(0f, -56f), new Vector2(350f, 70f), TextAlignmentOptions.Center, Hud.Muted);
                    Hud.Label(c, string.Join("\n", Deltas(s, second ? Catalog.SecondBranchLevel : Catalog.BranchLevel, code)), 18f, T, new Vector2(0f, -134f), new Vector2(350f, 150f), TextAlignmentOptions.Top);
                    Btn(c, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(330f, 52f), new Color(0.26f, 0.42f, 0.78f),
                        freeSplit ? L10n.T("menuPickFree") : L10n.T("menuPick", cost), freeSplit || coins >= cost,
                        () =>
                        {
                            _pendingLevel = second ? Catalog.SecondBranchLevel : Catalog.BranchLevel;
                            _pendingBranch = code; _pendingUntil = Time.unscaledTime + 2f;
                            Redraw(s);
                            Send(new PieceUpgradeRequest { Id = _id, Branch = code, FromLevel = s.Level, BranchOnly = freeSplit });
                        });
                }
            }
            else
            {
                var card = Hud.Box(p, T, new Vector2(0f, y), new Vector2(820f, 214f), Hud.Raised, 18f, pivot: T);
                var c = card.transform;
                string perk = Catalog.IsTurret(s.Type) && Catalog.Milestones(s.Level + 1) > Catalog.Milestones(s.Level)
                    ? "   <color=" + Hud.Hex(Hud.Gold) + ">" + L10n.T("milestone" + s.Type) + "</color>" : "";
                var nextLabel = Hud.Label(c, L10n.T("menuNext", s.Level + 1, BuildSystem.PieceName(s.Type, s.Level + 1)) + perk, 25f, T, new Vector2(0f, -16f), new Vector2(780f, 34f), TextAlignmentOptions.Center, Hud.Accent, bold: true);
                nextLabel.enableAutoSizing = true; nextLabel.fontSizeMin = 18f; nextLabel.fontSizeMax = 25f;
                Hud.Label(c, string.Join("　", Deltas(s, s.Level + 1, s.Branch)), 19f, T, new Vector2(-18f, -66f), new Vector2(560f, 100f), TextAlignmentOptions.TopLeft);
                Btn(c, new Vector2(1f, 0f), new Vector2(-20f, 20f), new Vector2(210f, 58f), new Color(0.26f, 0.42f, 0.78f),
                    L10n.T("menuUpgrade", cost), coins >= cost, () =>
                    {
                        _pendingLevel = s.Level + 1; _pendingBranch = s.Branch; _pendingUntil = Time.unscaledTime + 2f;
                        Redraw(s);
                        Send(new PieceUpgradeRequest { Id = _id, Branch = 1, FromLevel = s.Level });
                    });
            }

            // Fixed footer, same width and gap as every other panel.
            int repair = s.Hp < s.MaxHp ? Perks.MyCost(Catalog.RepairCost(s.Hp, s.MaxHp)) : 0;
            Btn(p, new Vector2(0.5f, 0f), new Vector2(-291f, 24f), new Vector2(186f, 52f), new Color(0.22f, 0.55f, 0.36f),
                repair > 0 ? L10n.T("menuRepair", repair) : L10n.T("menuHealthy"), repair > 0 && coins >= repair, () => Send(new PieceUpgradeRequest { Id = _id, Repair = true }));
            Btn(p, new Vector2(0.5f, 0f), new Vector2(-97f, 24f), new Vector2(186f, 52f), new Color(0.26f, 0.42f, 0.78f), L10n.T("menuMove"), true, () => { int id = _id; Close(); BuildSystem.Instance.StartMove(id); });
            Btn(p, new Vector2(0.5f, 0f), new Vector2(97f, 24f), new Vector2(186f, 52f), new Color(0.62f, 0.2f, 0.16f),
                _confirmRemove ? L10n.T("menuRemoveSure") : L10n.T("menuRemove", BuildSystem.RefundEstimate(s)), true, () =>
                {
                    if (!_confirmRemove) { _confirmRemove = true; return; }
                    InstanceFinder.ClientManager.Broadcast(new PieceRemoveRequest { Id = _id });
                    Close();
                });
            Btn(p, new Vector2(0.5f, 0f), new Vector2(291f, 24f), new Vector2(186f, 52f), new Color(0.3f, 0.33f, 0.38f), L10n.T("menuClose"), true, Close);
        }

        // The four numbers that matter for this piece, in the same order for every panel.
        private static List<(string, string)> StatsOf(PieceState s)
        {
            var list = new List<(string, string)>();
            if (Catalog.IsTurret(s.Type))
            {
                var t = Catalog.Turret(s.Type, s.Level, s.Branch);
                if (s.Type == PieceType.Bubble) { list.Add((L10n.T("statShield"), Mathf.RoundToInt(t.Shield * 100f) + "%")); list.Add((L10n.T("statRadius"), t.Range.ToString("0"))); }
                else if (s.Type == PieceType.Mender) { list.Add((L10n.T("statHeal"), t.Damage + "/s")); list.Add((L10n.T("statRadius"), t.Range.ToString("0"))); }
                else
                {
                    list.Add((L10n.T("statDamage"), t.Shots * t.Targets > 1 ? t.Damage + "×" + t.Shots * t.Targets : t.Damage.ToString()));
                    list.Add((L10n.T("statRate"), (1f / t.Interval).ToString("0.#") + "/s"));
                    list.Add((L10n.T("statRange"), t.Range.ToString("0")));
                }
            }
            list.Add((L10n.T("statHp"), s.MaxHp.ToString()));
            return list;
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
