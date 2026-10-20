using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    // Gear you can see during a round:
    //   pickup cards (right side): what you just picked up / what the chest gave, what it does, where to equip it;
    //   loadout bar (bottom right): the team's fortress gear and yours; a tile lights up when its legendary special goes off;
    //   "★ special" floating over the spot where it went off.
    public class GearCards : MonoBehaviour
    {
        public static GearCards Instance;
        private void Awake() => Instance = this;

        // ---------- pickup cards ----------

        // Like a loot toast: the gear's tile (same sign and rarity colour as in the vault), its name in that colour, what it does.
        private class Card { public Image Box; public CanvasGroup Group; public float Start, Until; }
        private struct Line { public string Glyph, Picture, Name, Detail; public Color Color; }
        private readonly List<Card> _cards = new List<Card>();
        private const float CardsTop = 0.8f, CardsFloor = 450f; // from 80% of the screen height down to just above the loadout bar

        public void ShowGear(Gear g, Rarity r)
        {
            var v = Loot.Instance.ClientVault;
            string detail = v.EffectFor(Loot.MyKey, g, r);
            if (r == Rarity.Legendary && !GearCatalog.IsActive(g)) detail += $"\n<color=#{GearCatalog.Hex(r)}>★ {L10n.T("gearLegend" + g)}</color>";
            Push(L10n.T("cardPicked"), new List<Line> { new Line { Glyph = L10n.T("glyph" + g), Picture = "Gear" + g, Name = v.LabelOf(g, r, Loot.MyKey), Detail = detail, Color = GearCatalog.Color(r) } }, 64f);
        }

        public void ShowChest(List<(Gear, Rarity)> items, string gameItem)
        {
            var v = Loot.Instance.ClientVault;
            var lines = items.OrderByDescending(i => i.Item2).Select(i => new Line
            {
                Glyph = L10n.T("glyph" + i.Item1), Picture = "Gear" + i.Item1, Name = v.LabelOf(i.Item1, i.Item2, Loot.MyKey), Color = GearCatalog.Color(i.Item2),
                Detail = (GearCatalog.IsWeapon(i.Item1) ? L10n.T("cardWeaponDrop") + "\n" : "") + v.EffectFor(Loot.MyKey, i.Item1, i.Item2),
            }).ToList();
            var f = gameItem?.Split('|');
            if (f != null && f.Length == 2 && int.TryParse(f[1], out int ir))
                lines.Add(new Line { Glyph = "◆", Name = $"[{GearCatalog.RarityName((Rarity)ir)}] {f[0]}", Detail = L10n.T("cardGameItem"), Color = GearCatalog.Color((Rarity)ir) });
            Push(L10n.T("cardChest"), lines, 44f, items.Any(i => !GearCatalog.IsWeapon(i.Item1)));
        }

        private void Push(string title, List<Line> lines, float tile, bool toVault = true)
        {
            if (!Hud.Font || lines.Count == 0) return;
            const float W = 480f, Pad = 16f;
            bool one = lines.Count == 1 && tile >= 60f;
            var box = Hud.Box(Hud.Root, new Vector2(1f, CardsTop), Vector2.zero, new Vector2(W, 100f), Hud.Panel, 16f, pivot: new Vector2(1f, 1f));
            var group = box.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var t = box.transform;
            var top = new Vector2(0f, 1f);
            Hud.Label(t, title, 15f, top, new Vector2(Pad, -10f), new Vector2(W - 2f * Pad, 20f), TextAlignmentOptions.TopLeft, Hud.Muted, pivot: top);
            float y = -36f, x = Pad + tile + 14f, w = W - x - Pad;
            foreach (var l in lines)
            {
                GlyphTile(t, top, new Vector2(Pad, y), tile, l.Glyph, l.Color, l.Picture);
                string name = $"<b>{l.Name}</b>";
                var nameText = Hud.Label(t, name, one ? 22f : 18f, top, new Vector2(x, y), new Vector2(w, 30f), TextAlignmentOptions.TopLeft, l.Color, pivot: top);
                float nh = nameText.GetPreferredValues(name, w, 0f).y;
                nameText.rectTransform.sizeDelta = new Vector2(w, nh);
                string said = Hud.Keys(l.Detail);
                var detail = Hud.Label(t, said, one ? 17f : 15f, top, new Vector2(x, y - nh - 2f), new Vector2(w, 200f), TextAlignmentOptions.TopLeft, one ? Hud.Ink : Hud.Muted, pivot: top);
                float dh = detail.GetPreferredValues(said, w, 0f).y;
                detail.rectTransform.sizeDelta = new Vector2(w, dh);
                y -= Mathf.Max(tile, nh + 2f + dh) + 10f;
            }
            if (toVault) { Hud.Label(t, Hud.Keys(L10n.T("cardWhere")), 14f, top, new Vector2(Pad, y - 2f), new Vector2(W - 2f * Pad, 20f), TextAlignmentOptions.TopLeft, Hud.Muted, pivot: top); y -= 20f; }
            box.rectTransform.sizeDelta = new Vector2(W, -y + 10f);
            _cards.Insert(0, new Card { Box = box, Group = group, Start = Time.time, Until = Time.time + 7f });
            while (_cards.Count > 3) { Destroy(_cards[_cards.Count - 1].Box.gameObject); _cards.RemoveAt(_cards.Count - 1); }
        }

        // The gear's tile wherever it shows during a round: its picture (or its sign) on a dark square of its rarity colour.
        private static Image GlyphTile(Transform parent, Vector2 anchor, Vector2 pos, float size, string glyph, Color c, string picture = null)
        {
            float round = size * 0.22f;
            var box = Hud.Box(parent, anchor, pos, new Vector2(size, size), new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 0.95f), round, pivot: anchor);
            Hud.Box(box.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), c, round, "Ui_outline");
            if (!Hud.Picture(box.transform, picture, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.84f, size * 0.84f)))
                Hud.Label(box.transform, glyph, size * 0.48f, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(size, size), TextAlignmentOptions.Center, c, bold: true);
            return box;
        }

        // ---------- legendary specials going off ----------

        private readonly Dictionary<Gear, float> _lit = new Dictionary<Gear, float>();
        private readonly List<(TextMeshProUGUI t, Vector3 at, float start)> _floats = new List<(TextMeshProUGUI, Vector3, float)>();

        public void Proc(Gear g, Vector3 at)
        {
            _lit[g] = Time.time;
            if (!Hud.Font) return;
            var t = Hud.Label(Hud.Root, $"★ {L10n.T("legendShort" + g)}", 24f, Vector2.zero, Vector2.zero, new Vector2(300f, 34f), TextAlignmentOptions.Center, Hud.Gold, bold: true, outline: true);
            _floats.Add((t, at + Vector3.up * 1.5f, Time.time));
            AutoTest.Count("proc_" + g);
        }

        // ---------- loadout bar ----------

        // Bottom right, like a buff bar: the team's fortress gear in one row, yours in the next, each a small tile with the
        // same sign and rarity colour as in the vault; a tile pops and glows white when its legendary special goes off.
        private RectTransform _bar;
        private string _barKey = "";
        private readonly Dictionary<Gear, (RectTransform tile, Image glow)> _tiles = new Dictionary<Gear, (RectTransform, Image)>();
        private const float BarTile = 46f;

        private void LateUpdate()
        {
            float now = Time.time;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (!c.Box || now > c.Until) { if (c.Box) Destroy(c.Box.gameObject); _cards.RemoveAt(i); continue; }
            }
            float y = 0f, room = ((RectTransform)Hud.Root).rect.height * CardsTop - CardsFloor;
            foreach (var c in _cards)
            {
                float a = Mathf.Clamp01((now - c.Start) / 0.25f), z = Mathf.Clamp01((c.Until - now) / 0.5f);
                c.Group.alpha = Mathf.Min(a, z);
                c.Box.gameObject.SetActive(!Hud.ModalOpen && y + c.Box.rectTransform.sizeDelta.y <= room);
                c.Box.rectTransform.anchoredPosition = new Vector2(-22f + 40f * (1f - a), -y);
                y += c.Box.rectTransform.sizeDelta.y + 10f;
            }

            var cam = GameInfo.CurCamera;
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var (t, at, start) = _floats[i];
                float k = (now - start) / 1.2f;
                if (!t || k >= 1f || !cam) { if (t) Destroy(t.gameObject); _floats.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(at + Vector3.up * k);
                t.gameObject.SetActive(sp.z > 0f);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
                t.rectTransform.anchoredPosition = Vector2.zero;
                t.alpha = 1f - k * k;
            }

            DrawLoadout();
        }

        private void DrawLoadout()
        {
            var v = Loot.Instance ? Loot.Instance.ClientVault : null;
            ulong me = Player.LocalPlayer ? Loot.KeyOf(Player.LocalPlayer) : 0;
            var mine = v != null && v.Personal.TryGetValue(me, out var l) ? l.Where(x => !GearCatalog.IsActive(x.Item1)).ToList() : new List<(Gear, Rarity)>();
            bool show = v != null && Siege.Instance && Siege.Instance.ViewActive && Hud.Font && (v.Fortress.Count > 0 || mine.Count > 0) && !Hud.ModalOpen;
            if (!show) { if (_bar) _bar.gameObject.SetActive(false); return; }
            // Rebuild only when the loadout changes.
            string key = string.Join(",", v.Fortress.Select(x => $"{x.Item1}{x.Item2}{v.EnhanceOf(x.Item1)}")) + "|" + string.Join(",", mine.Select(x => $"{x.Item1}{x.Item2}{v.EnhanceOf(x.Item1)}"));
            if (!_bar || key != _barKey)
            {
                if (_bar) Destroy(_bar.gameObject);
                _tiles.Clear();
                _barKey = key;
                _bar = Hud.Node(Hud.Root, "Loadout", new Vector2(1f, 0f), Vector2.zero, new Vector2(320f, 120f), new Vector2(1f, 0f));
                void Row(string label, List<(Gear, Rarity)> items, float y)
                {
                    if (items.Count == 0) return;
                    Hud.Label(_bar, label, 15f, new Vector2(1f, 0f), new Vector2(-(items.Count * (BarTile + 6f)) - 8f, y + BarTile * 0.5f), new Vector2(60f, 20f), TextAlignmentOptions.Right, Hud.Muted, pivot: new Vector2(1f, 0.5f));
                    for (int i = 0; i < items.Count; i++)
                    {
                        var (g, r) = items[i];
                        var c = GearCatalog.Color(r);
                        var box = GlyphTile(_bar, new Vector2(1f, 0f), new Vector2(-(items.Count - 1 - i) * (BarTile + 6f), y), BarTile, L10n.T("glyph" + g), c, "Gear" + g);
                        if (r == Rarity.Legendary && v.EnhanceOf(g) > 0)
                            Hud.Label(box.transform, "+" + v.EnhanceOf(g), 12f, new Vector2(1f, 1f), new Vector2(-3f, -2f), new Vector2(30f, 14f), TextAlignmentOptions.TopRight, c, bold: true, pivot: new Vector2(1f, 1f));
                        var glow = Hud.Box(box.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BarTile, BarTile), new Color(1f, 1f, 1f, 0f), 10f);
                        glow.raycastTarget = false;
                        _tiles[g] = (box.rectTransform, glow);
                    }
                }
                Row(L10n.T("loadoutTeam"), v.Fortress.ToList(), BarTile + 8f);
                Row(L10n.T("loadoutMine"), mine, 0f);
            }
            _bar.gameObject.SetActive(true);
            // Sit above the active slot when there is one.
            _bar.anchoredPosition = new Vector2(-24f, Actives.Instance && Actives.Instance.ClientHasActive ? 336f : 264f);
            foreach (var kv in _tiles)
            {
                float k = _lit.TryGetValue(kv.Key, out float at) ? Mathf.Clamp01(1f - (Time.time - at) / 0.8f) : 0f;
                kv.Value.tile.localScale = Vector3.one * (1f + 0.25f * k);
                kv.Value.glow.color = new Color(1f, 1f, 1f, 0.6f * k);
            }
        }
    }
}
