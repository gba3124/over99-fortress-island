using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    // Gear you can see during a round:
    //   pickup cards (right side): what you just picked up / what the chest gave, what it does, where to equip it;
    //   loadout panel (bottom right): the team's fortress gear and yours; a name lights up when its legendary special goes off;
    //   "★ special" floating over the spot where it went off.
    public class GearCards : MonoBehaviour
    {
        public static GearCards Instance;
        private void Awake() => Instance = this;

        // ---------- pickup cards ----------

        private class Card { public Image Box; public CanvasGroup Group; public float Start, Until; }
        private readonly List<Card> _cards = new List<Card>();

        public void ShowGear(Gear g, Rarity r)
        {
            var v = Loot.Instance.ClientVault;
            var lines = new List<string> { $"<size=115%><b>{v.LabelOf(g, r)}</b></size>", GearCatalog.Effect(g, r, v.ValueOf(g, r)) };
            if (r == Rarity.Legendary && !GearCatalog.IsActive(g)) lines.Add($"<color=#{GearCatalog.Hex(r)}>★ {L10n.T("gearLegend" + g)}</color>");
            Push(L10n.T("cardPicked"), lines, GearCatalog.Color(r));
        }

        public void ShowChest(List<(Gear, Rarity)> items, string gameItem)
        {
            var v = Loot.Instance.ClientVault;
            var lines = items.Select(i => $"{v.LabelOf(i.Item1, i.Item2)}  <size=85%><color=#B9C2CC>{GearCatalog.Effect(i.Item1, i.Item2, v.ValueOf(i.Item1, i.Item2))}</color></size>").ToList();
            if (gameItem != null)
            {
                var f = gameItem.Split('|');
                if (f.Length == 2 && int.TryParse(f[1], out int ir)) lines.Add($"<color=#{GearCatalog.Hex((Rarity)ir)}>[{GearCatalog.RarityName((Rarity)ir)}] {f[0]}</color>  <size=85%>{L10n.T("cardGameItem")}</size>");
            }
            var best = items.Count > 0 ? items.Max(i => i.Item2) : Rarity.Common;
            Push(L10n.T("cardChest"), lines, GearCatalog.Color(best));
        }

        private void Push(string title, List<string> lines, Color stripe)
        {
            if (!Hud.Font) return;
            var box = Hud.Box(Hud.Root, new Vector2(1f, 0.66f), Vector2.zero, new Vector2(470f, 100f), Hud.Panel, 16f, pivot: new Vector2(1f, 1f));
            var group = box.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var t = box.transform;
            var head = Hud.Label(t, title, 18f, new Vector2(0f, 1f), new Vector2(22f, -10f), new Vector2(430f, 24f), TextAlignmentOptions.TopLeft, Hud.Muted, pivot: new Vector2(0f, 1f));
            string body = string.Join("\n", lines);
            var text = Hud.Label(t, body, 20f, new Vector2(0f, 1f), new Vector2(22f, -36f), new Vector2(430f, 300f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
            var foot = Hud.Label(t, Hud.Keys(L10n.T("cardWhere")), 16f, new Vector2(0f, 0f), new Vector2(22f, 10f), new Vector2(430f, 22f), TextAlignmentOptions.BottomLeft, Hud.Muted, pivot: new Vector2(0f, 0f));
            float h = text.GetPreferredValues(body, 430f, 0f).y + 76f;
            box.rectTransform.sizeDelta = new Vector2(470f, h);
            Hud.Box(t, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, h - 24f), stripe, 3f, pivot: new Vector2(0f, 0.5f));
            _cards.Insert(0, new Card { Box = box, Group = group, Start = Time.time, Until = Time.time + 7f });
            while (_cards.Count > 3) { Destroy(_cards[_cards.Count - 1].Box.gameObject); _cards.RemoveAt(_cards.Count - 1); }
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

        // ---------- loadout panel ----------

        private Image _loadout;
        private TextMeshProUGUI _loadoutText;

        private void LateUpdate()
        {
            float now = Time.time;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (!c.Box || now > c.Until) { if (c.Box) Destroy(c.Box.gameObject); _cards.RemoveAt(i); continue; }
            }
            float y = 0f;
            foreach (var c in _cards)
            {
                float a = Mathf.Clamp01((now - c.Start) / 0.25f), z = Mathf.Clamp01((c.Until - now) / 0.5f);
                c.Group.alpha = Mathf.Min(a, z);
                c.Box.gameObject.SetActive(!Hud.ModalOpen);
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
            if (!show) { if (_loadout) _loadout.gameObject.SetActive(false); return; }
            if (!_loadout)
            {
                _loadout = Hud.Box(Hud.Root, new Vector2(1f, 0f), new Vector2(-24f, 310f), new Vector2(320f, 80f), Hud.Panel, 16f, pivot: new Vector2(1f, 0f));
                _loadoutText = Hud.Label(_loadout.transform, "", 18f, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(296f, 200f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
            }
            _loadout.gameObject.SetActive(true);
            string Name((Gear g, Rarity r) x)
            {
                bool lit = _lit.TryGetValue(x.g, out float at) && Time.time - at < 0.8f;
                string n = GearCatalog.Name(x.g) + (x.r == Rarity.Legendary && v.EnhanceOf(x.g) > 0 ? "+" + v.EnhanceOf(x.g) : "");
                return lit ? $"<color=#FFFFFF><b>★{n}</b></color>" : $"<color=#{GearCatalog.Hex(x.r)}>{n}</color>";
            }
            var rows = new List<string>();
            if (v.Fortress.Count > 0) rows.Add($"<color={Hud.Hex(Hud.Muted)}>{L10n.T("loadoutTeam")}</color>  " + string.Join("、", v.Fortress.Select(Name)));
            if (mine.Count > 0) rows.Add($"<color={Hud.Hex(Hud.Muted)}>{L10n.T("loadoutMine")}</color>  " + string.Join("、", mine.Select(Name)));
            string s = string.Join("\n", rows);
            if (_loadoutText.text != s)
            {
                _loadoutText.text = s;
                _loadout.rectTransform.sizeDelta = new Vector2(320f, _loadoutText.GetPreferredValues(s, 296f, 0f).y + 22f);
            }
            // Sit above the active slot when there is one.
            _loadout.rectTransform.anchoredPosition = new Vector2(-24f, Actives.Instance && Actives.Instance.ClientHasActive ? 336f : 264f);
        }
    }
}
