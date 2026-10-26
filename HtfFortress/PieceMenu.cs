using System;
using System.Collections.Generic;
using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HtfFortress
{
    // E on a piece (in build mode or walking up to it), right click in build mode, or U on a turret about to choose a branch:
    // what it is and how healthy, its way up (the Lv3 branch, the Lv9 split, the Lv5/10/15 perks), today's numbers next to the
    // next level's with the one button that buys it, or each branch with a picture of its head; then repair, move, take down.
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

        // ---------- layout: header, the way up (turrets), next level or the two branches, footer ----------

        private static readonly Vector2 TL = new Vector2(0f, 1f), Mid = new Vector2(0.5f, 0.5f);
        private static readonly Color Blue = new Color(0.26f, 0.42f, 0.78f), Green = new Color(0.22f, 0.55f, 0.36f),
                                      Red = new Color(0.62f, 0.2f, 0.16f), Slate = new Color(0.29f, 0.33f, 0.41f);
        private const float W = 900f, HeaderH = 152f, TrackH = 118f, NextH = 196f, ChoiceH = 40f + 304f, Gap = 14f, FooterH = 52f + 24f;
        private const int TrackLevels = 15; // Lv1 to the last perk; a turret past it stands on the last stop

        private void Redraw(PieceState s)
        {
            if (_pendingLevel > s.Level && Time.unscaledTime < _pendingUntil)
            {
                s.Level = _pendingLevel; s.Branch = _pendingBranch;
                s.MaxHp = Catalog.MaxHp(s.Type, s.Level); s.Hp = Mathf.Min(s.Hp, s.MaxHp);
            }
            if (_panel) Destroy(_panel.gameObject);
            bool turret = Catalog.IsTurret(s.Type);
            bool branching = Catalog.HasBranches(s.Type) && s.Level == Catalog.BranchLevel - 1;
            bool freeSplit = Catalog.HasSecondBranch(s.Type) && s.Level >= Catalog.SecondBranchLevel && s.Branch <= 2;
            bool second = Catalog.HasSecondBranch(s.Type) && (s.Level == Catalog.SecondBranchLevel - 1 || freeSplit);
            bool choosing = branching || second;
            float h = HeaderH + (turret ? TrackH + Gap : 0f) + (choosing ? ChoiceH : NextH) + Gap + FooterH;
            var dim = Hud.Box(_canvas.transform, Mid, Vector2.zero, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.42f), 0f);
            dim.raycastTarget = true;
            _panel = dim.rectTransform;
            var p = Hud.Box(_panel, Mid, new Vector2(0f, 10f), new Vector2(W, h), Hud.PanelSolid, 26f).transform;
            int coins = Siege.Instance.ViewCoins;
            int cost = Perks.MyTowerUpgradeCost(Catalog.UpgradeCost(s.Type, s.Level));

            Header(p, s);
            float y = HeaderH;
            if (turret) { Track(p, s, y); y += TrackH + Gap; }
            if (choosing) { Choices(p, s, y, second, freeSplit, cost, coins); y += ChoiceH + Gap; }
            else { Next(p, s, y, cost, coins); y += NextH + Gap; }
            Footer(p, s, y, coins);
        }

        // Its picture (a turret's own head once it has a branch), name and branch, what it does, health, level; × closes.
        private void Header(Transform p, PieceState s)
        {
            Hud.Picture(p, PictureOf(s), TL, new Vector2(22f, -14f), new Vector2(124f, 124f), TL);
            string branch = s.Branch > 0 ? "  " + Chip(Tag(s.Branch) + " " + Catalog.BranchName(s.Type, s.Branch), Hud.Accent, 60) : "";
            Hud.Label(p, BuildSystem.PieceName(s.Type, s.Level) + branch, 34f, TL, new Vector2(158f, -22f), new Vector2(520f, 42f), TextAlignmentOptions.Left, bold: true);
            Hud.Label(p, s.Branch > 0 ? Catalog.BranchDesc(s.Type, s.Branch) : L10n.T("pieceDesc" + s.Type), 18f, TL, new Vector2(158f, -72f), new Vector2(700f, 26f), TextAlignmentOptions.Left, Hud.Muted);
            float hp = s.MaxHp > 0 ? (float)s.Hp / s.MaxHp : 1f;
            Hud.Bar.Make(p, TL, new Vector2(158f, -108f), new Vector2(420f, 14f), Hud.HealthColor(hp), TL).Set(hp);
            Hud.Label(p, s.Hp + " / " + s.MaxHp, 18f, TL, new Vector2(592f, -103f), new Vector2(220f, 24f), TextAlignmentOptions.Left, Hud.Muted);
            Hud.Label(p, Lv(s.Level), 44f, new Vector2(1f, 1f), new Vector2(-84f, -18f), new Vector2(240f, 52f), TextAlignmentOptions.Right, Hud.Gold, bold: true);
            Btn(p, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(46f, 46f), new Color(0.165f, 0.19f, 0.23f), "×", true, Close, new Vector2(1f, 1f), 30f, Hud.Muted);
        }

        // Lv1..Lv15 as a row of stops, the branch (Lv3), the second split (Lv9) and the perks (Lv5/10/15) marked, the ones
        // behind lit, where it stands ringed; under it, the next two things it gets.
        private void Track(Transform p, PieceState s, float top)
        {
            var box = Hud.Box(p, TL, new Vector2(26f, -top), new Vector2(848f, TrackH), Hud.Raised, 16f, pivot: TL).transform;
            const float x0 = 46f, dx = 54f, y = -48f;
            int at = Mathf.Min(s.Level, TrackLevels);
            Hud.Box(box, TL, new Vector2(x0, y), new Vector2(dx * (TrackLevels - 1), 4f), new Color(1f, 1f, 1f, 0.12f), 2f, pivot: new Vector2(0f, 0.5f));
            if (at > 1) Hud.Box(box, TL, new Vector2(x0, y), new Vector2(dx * (at - 1), 4f), Hud.Accent, 2f, pivot: new Vector2(0f, 0.5f));
            Hud.Label(box, "Lv", 13f, TL, new Vector2(8f, y - 28f), new Vector2(28f, 18f), TextAlignmentOptions.Left, Hud.Muted, pivot: new Vector2(0f, 0.5f));
            var ahead = new Color(1f, 1f, 1f, 0.36f);
            for (int i = 1; i <= TrackLevels; i++)
            {
                float x = x0 + (i - 1) * dx;
                var kind = Stop(i);
                bool past = i < at, now = i == at;
                Color mark = kind == StopKind.Perk ? Hud.Gold : Hud.Accent;
                if (now)
                {
                    Dot(box, x, y, 26f, Hud.Gold); Dot(box, x, y, 20f, Hud.Raised); Dot(box, x, y, 10f, Hud.Gold);
                }
                else if (kind == StopKind.Perk) Hud.Label(box, "★", 23f, TL, new Vector2(x, y + 1f), new Vector2(32f, 32f), TextAlignmentOptions.Center, past ? mark : Fade(mark, 0.45f), pivot: Mid);
                else if (kind != StopKind.Level)
                {
                    Diamond(box, x, y, past ? 14f : 16f, past ? mark : Fade(mark, 0.8f));
                    if (!past) Diamond(box, x, y, 11f, Hud.Raised);
                }
                else Dot(box, x, y, past ? 12f : 10f, past ? Hud.Accent : new Color(1f, 1f, 1f, 0.28f));
                string word = now ? L10n.T("nodeNow") : kind == StopKind.Branch ? L10n.T("nodeBranch") : kind == StopKind.Split ? L10n.T("nodeSplit") : kind == StopKind.Perk ? L10n.T("nodePerk") : null;
                if (word != null) Hud.Label(box, word, 14f, TL, new Vector2(x, y + 28f), new Vector2(58f, 20f), TextAlignmentOptions.Center, now ? Hud.Gold : past ? Hud.Muted : Fade(mark, 0.75f), now, pivot: Mid);
                string n = now ? s.Level.ToString() : i.ToString();
                Hud.Label(box, n, now ? 14f : 13f, TL, new Vector2(x, y - 28f), new Vector2(44f, 18f), TextAlignmentOptions.Center, now ? Hud.Gold : past ? Hud.Muted : ahead, now, pivot: Mid);
            }
            Hud.Label(box, Upcoming(s), 15f, TL, new Vector2(14f, -90f), new Vector2(820f, 22f), TextAlignmentOptions.Left, Hud.Muted);
        }

        private enum StopKind { Level, Branch, Split, Perk }
        private static StopKind Stop(int level) =>
            level == Catalog.BranchLevel ? StopKind.Branch : level == Catalog.SecondBranchLevel ? StopKind.Split
            : Catalog.Milestones(level) > Catalog.Milestones(level - 1) ? StopKind.Perk : StopKind.Level;

        // "Next: Lv9 second split: Typhoon or Hail　Lv10 perk: one more fish at a time"
        private static string Upcoming(PieceState s)
        {
            var next = new List<string>();
            int first = Catalog.FirstOf(s.Branch);
            for (int l = s.Level + 1; l <= TrackLevels && next.Count < 2; l++)
                switch (Stop(l))
                {
                    case StopKind.Branch: next.Add(Paint(L10n.T("trackBranch", l, Catalog.BranchName(s.Type, 1), Catalog.BranchName(s.Type, 2)), Hud.Accent)); break;
                    case StopKind.Split:
                        next.Add(Paint(s.Branch > 0 ? L10n.T("trackSplit", l, Catalog.BranchName(s.Type, Catalog.BranchCode(first, 1)), Catalog.BranchName(s.Type, Catalog.BranchCode(first, 2)))
                                                    : L10n.T("trackSplitLater", l), Hud.Accent));
                        break;
                    case StopKind.Perk: next.Add(Paint(L10n.T("trackPerk", l, Perk(s.Type)), Hud.Gold)); break;
                }
            return next.Count > 0 ? L10n.T("trackNext", string.Join("　", next)) : L10n.T("trackDone");
        }

        // Today's numbers next to the next level's, and the one button that buys it (short of money: by how much).
        private void Next(Transform p, PieceState s, float top, int cost, int coins)
        {
            var box = Hud.Box(p, TL, new Vector2(26f, -top), new Vector2(848f, NextH), Hud.Raised, 16f, pivot: TL).transform;
            int to = s.Level + 1;
            string rename = BuildSystem.PieceName(s.Type, to) != BuildSystem.PieceName(s.Type, s.Level) ? " " + BuildSystem.PieceName(s.Type, to) : ""; // wood -> stone -> iron
            string perk = Catalog.IsTurret(s.Type) && Stop(to) == StopKind.Perk ? "　" + Paint("★ " + Perk(s.Type), Hud.Gold) : "";
            Hud.Label(box, L10n.T("menuNow"), 15f, TL, new Vector2(160f, -12f), new Vector2(140f, 22f), TextAlignmentOptions.Right, Hud.Muted);
            Hud.Label(box, Lv(to) + rename + perk, 15f, TL, new Vector2(380f, -12f), new Vector2(444f, 22f), TextAlignmentOptions.Left, Hud.Accent, bold: true);
            var rows = Rows(s, to, s.Branch);
            float pitch = rows.Count > 4 ? 30f : 36f;
            for (int i = 0; i < rows.Count; i++)
            {
                var (label, now, next) = rows[i];
                float y = -40f - i * pitch;
                Hud.Label(box, label, 19f, TL, new Vector2(28f, y), new Vector2(140f, 28f), TextAlignmentOptions.Left, Hud.Muted);
                Hud.Label(box, now, 22f, TL, new Vector2(160f, y), new Vector2(140f, 28f), TextAlignmentOptions.Right, bold: true);
                Hud.Label(box, "→", 18f, TL, new Vector2(320f, y), new Vector2(40f, 28f), TextAlignmentOptions.Center, Hud.Muted);
                Hud.Label(box, next, 22f, TL, new Vector2(380f, y), new Vector2(170f, 28f), TextAlignmentOptions.Left, next == now ? Hud.Muted : Hud.Good, bold: true);
            }
            bool afford = coins >= cost;
            Btn(box, TL, new Vector2(562f, -44f), new Vector2(262f, 108f), Blue, L10n.T("menuUpgradeTo", Lv(to)) + "\n" + Paint("$" + cost, afford ? Hud.Gold : Hud.Bad), afford, () =>
            {
                _pendingLevel = s.Level + 1; _pendingBranch = s.Branch; _pendingUntil = Time.unscaledTime + 2f;
                Redraw(s);
                Send(new PieceUpgradeRequest { Id = _id, Branch = 1, FromLevel = s.Level });
            }, TL, 24f);
            if (!afford) Hud.Label(box, L10n.T("menuShort", cost - coins), 16f, TL, new Vector2(562f, -158f), new Vector2(262f, 24f), TextAlignmentOptions.Center, Hud.Bad);
        }

        // The two ways it can go (into Lv3, or into Lv9 along the branch it took), each with its head, what it does and its numbers.
        private void Choices(Transform p, PieceState s, float top, bool second, bool freeSplit, int cost, int coins)
        {
            int first = s.Branch > 0 ? Catalog.FirstOf(s.Branch) : 1;
            string title = L10n.T(second ? "menuChooseSecond" : "menuChooseFirst"), note = L10n.T(second ? "menuChooseNote2" : "menuChooseNote", Catalog.SecondBranchLevel);
            Hud.Label(p, $"<b>{title}</b><size=67%>{Paint("　" + note, Hud.Muted)}</size>", 24f, TL, new Vector2(28f, -top), new Vector2(846f, 32f), TextAlignmentOptions.Left, Hud.Accent);
            int toLevel = freeSplit ? s.Level : second ? Catalog.SecondBranchLevel : Catalog.BranchLevel; // a late split keeps its level
            bool afford = freeSplit || coins >= cost;
            for (int k = 0; k < 2; k++)
            {
                int code = second ? Catalog.BranchCode(first, k + 1) : k + 1;
                if (k == 0) BuildSystem.Log.LogInfo($"piece menu {s.Type} Lv{s.Level} branch {s.Branch}: split options {code}/{(second ? Catalog.BranchCode(first, 2) : 2)} ({Catalog.BranchName(s.Type, code)} / {Catalog.BranchName(s.Type, second ? Catalog.BranchCode(first, 2) : 2)})");
                var c = Hud.Box(p, TL, new Vector2(26f + k * 432f, -(top + 40f)), new Vector2(416f, 304f), Hud.Raised, 18f, pivot: TL).transform;
                Hud.Picture(c, "Piece" + Catalog.ModuleModel(s.Type, code), TL, new Vector2(10f, -10f), new Vector2(140f, 140f), TL);
                Hud.Label(c, Chip(Tag(code), Hud.Gold, 57) + "  " + Paint(Catalog.BranchName(s.Type, code), Hud.Accent), 30f, TL, new Vector2(158f, -14f), new Vector2(246f, 40f), TextAlignmentOptions.Left, bold: true);
                var desc = Hud.Label(c, Catalog.BranchDesc(s.Type, code), 19f, TL, new Vector2(158f, -58f), new Vector2(246f, 46f), TextAlignmentOptions.TopLeft);
                desc.enableAutoSizing = true; desc.fontSizeMin = 13f; desc.fontSizeMax = 19f;
                var rows = Rows(s, toLevel, code);
                float pitch = rows.Count > 4 ? 21f : 26f;
                for (int i = 0; i < rows.Count; i++)
                {
                    var (label, now, next) = rows[i];
                    Hud.Label(c, $"{Paint(label, Hud.Muted)}  {now} → {(next == now ? next : "<b>" + Paint(next, Hud.Good) + "</b>")}", rows.Count > 4 ? 16f : 17f,
                              TL, new Vector2(158f, -106f - i * pitch), new Vector2(250f, pitch), TextAlignmentOptions.Left);
                }
                if (!second) Hud.Label(c, L10n.T("menuLater", Catalog.SecondBranchLevel, Catalog.BranchName(s.Type, Catalog.BranchCode(code, 1)), Catalog.BranchName(s.Type, Catalog.BranchCode(code, 2))),
                                       15f, TL, new Vector2(16f, -212f), new Vector2(384f, 22f), TextAlignmentOptions.Left, Hud.Muted);
                string pick = freeSplit ? L10n.T("menuPickFree") : L10n.T("menuPickNamed", Catalog.BranchName(s.Type, code)) + "　" + Paint("$" + cost, afford ? Hud.Gold : Hud.Bad);
                Btn(c, TL, new Vector2(14f, -240f), new Vector2(388f, 52f), Blue, pick, afford, () =>
                {
                    _pendingLevel = second ? Catalog.SecondBranchLevel : Catalog.BranchLevel;
                    _pendingBranch = code; _pendingUntil = Time.unscaledTime + 2f;
                    Redraw(s);
                    Send(new PieceUpgradeRequest { Id = _id, Branch = code, FromLevel = s.Level, BranchOnly = freeSplit });
                }, TL);
            }
        }

        // Repair (only when hurt), move, take down (twice to be sure); right click closes.
        private void Footer(Transform p, PieceState s, float top, int coins)
        {
            float x = 26f;
            int repair = s.Hp < s.MaxHp ? Perks.MyCost(Catalog.RepairCost(s.Hp, s.MaxHp)) : 0;
            if (repair > 0)
            {
                Btn(p, TL, new Vector2(x, -top), new Vector2(196f, 52f), Green, L10n.T("menuRepair", Paint("$" + repair, coins >= repair ? Hud.Gold : Hud.Bad)), coins >= repair,
                    () => Send(new PieceUpgradeRequest { Id = _id, Repair = true }), TL, 21f);
                x += 210f;
            }
            Btn(p, TL, new Vector2(x, -top), new Vector2(130f, 52f), Slate, L10n.T("menuMove"), true, () => { int id = _id; Close(); BuildSystem.Instance.StartMove(id); }, TL, 21f);
            x += 144f;
            Btn(p, TL, new Vector2(x, -top), new Vector2(236f, 52f), Red, _confirmRemove ? L10n.T("menuRemoveSure") : L10n.T("menuRemove", BuildSystem.RefundEstimate(s)), true, () =>
            {
                if (!_confirmRemove) { _confirmRemove = true; return; }
                InstanceFinder.ClientManager.Broadcast(new PieceRemoveRequest { Id = _id });
                Close();
            }, TL, 20f);
            Hud.Label(p, Hud.Keys(L10n.T("menuCloseHint")), 16f, new Vector2(1f, 1f), new Vector2(-28f, -(top + 14f)), new Vector2(240f, 24f), TextAlignmentOptions.Right, Hud.Muted);
        }

        // ---------- shared with the look card and the build bar ----------

        // The piece as it stands: a wall shows its material, a turret with a branch its class's head (tools/make_icons.py).
        public static string PictureOf(PieceState s) =>
            s.Type == PieceType.Wall ? (s.Level >= 3 ? "PieceWallMetal" : s.Level == 2 ? "PieceWallStone" : "PieceWall")
            : "Piece" + (s.Branch > 0 && Catalog.HasModules(s.Type) ? Catalog.ModuleModel(s.Type, s.Branch) : s.Type.ToString());

        // The numbers that matter for this piece, in the same order everywhere: (what, now, at `toLevel` along `branch`).
        internal static List<(string label, string now, string next)> Rows(PieceState s, int toLevel, int branch)
        {
            var rows = new List<(string, string, string)>();
            if (Catalog.IsTurret(s.Type))
            {
                var a = Catalog.Turret(s.Type, s.Level, s.Branch);
                var b = Catalog.Turret(s.Type, toLevel, branch);
                if (s.Type == PieceType.Bubble)
                {
                    rows.Add((L10n.T("statShield"), Mathf.RoundToInt(a.Shield * 100f) + "%", Mathf.RoundToInt(b.Shield * 100f) + "%"));
                    rows.Add((L10n.T("statRadius"), a.Range.ToString("0"), b.Range.ToString("0")));
                }
                else if (s.Type == PieceType.Mender)
                {
                    rows.Add((L10n.T("statHeal"), a.Damage + "/s", b.Damage + "/s"));
                    rows.Add((L10n.T("statRadius"), a.Range.ToString("0"), b.Range.ToString("0")));
                }
                else
                {
                    if (s.Type == PieceType.Flame) rows.Add((L10n.T("statBurn"), a.Burn.ToString("0") + "/s", b.Burn.ToString("0") + "/s"));
                    rows.Add((L10n.T("statDamage"), Dmg(a), Dmg(b)));
                    if (s.Type == PieceType.Tesla) rows.Add((L10n.T("statChain"), a.Chain.ToString(), b.Chain.ToString()));
                    rows.Add((L10n.T("statRate"), Rate(a), Rate(b)));
                    rows.Add((L10n.T("statRange"), a.Range.ToString("0"), b.Range.ToString("0")));
                }
            }
            // Health scales with the same gear bonus the piece already has.
            float ratio = Catalog.MaxHp(s.Type, s.Level) > 0 ? (float)s.MaxHp / Catalog.MaxHp(s.Type, s.Level) : 1f;
            rows.Add((L10n.T("statHp"), s.MaxHp.ToString(), Mathf.RoundToInt(Catalog.MaxHp(s.Type, toLevel) * ratio).ToString()));
            if (s.Type == PieceType.Spikes) rows.Add((L10n.T("statDamage"), Catalog.SpikeDamage(s.Level).ToString(), Catalog.SpikeDamage(toLevel).ToString()));
            return rows;
        }

        private static string Dmg(Catalog.TurretStats t) => t.Shots * t.Targets > 1 ? $"{t.Damage}×{t.Shots * t.Targets}" : t.Damage.ToString(); // ×shells or ×fish at once
        private static string Rate(Catalog.TurretStats t) => (1f / t.Interval).ToString("0.#") + "/s";

        internal static string Lv(int level) => L10n.T("lv", level);
        internal static string Tag(int branch) => (Catalog.FirstOf(branch) == 1 ? "A" : "B") + (branch > 2 ? Catalog.SecondOf(branch).ToString() : ""); // A, B2...
        internal static string Perk(PieceType t) => L10n.T("milestone" + t).TrimStart('＋', '+', ' ');
        internal static string Paint(string text, Color c) => $"<color={Hud.Hex(c)}>{text}</color>";
        // A tag on a tinted ground inside a line of text ("A Storm"), at `size` percent of the line's size.
        internal static string Chip(string text, Color c, int size) =>
            $"<size={size}%><mark=#{ColorUtility.ToHtmlStringRGB(c)}30 padding=\"10,10,4,4\">{Paint(" " + text + " ", c)}</mark></size>";

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);
        private static void Dot(Transform t, float x, float y, float d, Color c) => Hud.Box(t, TL, new Vector2(x, y), new Vector2(d, d), c, d / 2f, pivot: Mid);
        private static void Diamond(Transform t, float x, float y, float d, Color c) =>
            Hud.Box(t, TL, new Vector2(x, y), new Vector2(d, d), c, 2f, pivot: Mid).rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        private void Send(PieceUpgradeRequest r)
        {
            if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(r);
            if (!r.Repair) BuildSystem.Instance.AskedUp(r.Id);
            Fx.Sound("Click", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.6f, AudioDistance.Short);
        }

        private static void Btn(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c, string text, bool enabled, Action onClick,
                                Vector2? pivot = null, float font = 22f, Color? ink = null)
        {
            var img = Hud.Box(parent, anchor, pos, size, c, 14f, pivot: pivot ?? new Vector2(0.5f, anchor.y));
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            b.colors = colors;
            b.interactable = enabled;
            b.onClick.AddListener(() => onClick());
            Hud.Label(img.transform, text, font, Mid, Vector2.zero, size - new Vector2(12f, 6f), TextAlignmentOptions.Center, ink, bold: true);
        }

        // Auto-test hooks.
        public void DebugUpgrade(int branch) => Send(new PieceUpgradeRequest { Id = _id, Branch = branch, FromLevel = BuildSystem.Instance.LevelOf(_id) });
    }
}
