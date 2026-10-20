using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    // Where your teammates are (the playtest, mid-round: 「幹 我完全看不到你們在哪」): over each other player's head a small mark
    // drawn over everything, with their name and how far; red and blinking, "carry them back", while they're down (in a round
    // only a teammate can bring them back: FortressPlugin.NoSelfRespawn), over their body wherever it's carried; an arrow at
    // the screen's edge when they're off it. Faint when they're right by you or under your crosshair (you can see them),
    // never while they're down. Around the fortress only. Each player's own: config Players/TeammateMarkers (Show).
    public class Markers : MonoBehaviour
    {
        public static ConfigEntry<bool> Show;
        private static readonly Color Mate = new Color(0.45f, 0.85f, 1f);

        private class Mark
        {
            public RectTransform Root;
            public Image Dot, Arrow;
            public TextMeshProUGUI Text;
            public CanvasGroup Group;
            public string Name;
            public int Metres = -1, Seen;
            public bool Down, Edge = true;
            public Item Body;
            public float NextBody, NextText;
        }
        private readonly Dictionary<int, Mark> _marks = new Dictionary<int, Mark>();
        private readonly HashSet<int> _here = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private float _nextSweep;

        private void LateUpdate()
        {
            var cam = GameInfo.CurCamera;
            var me = Player.LocalPlayer;
            var island = FortressIsland.Instance;
            bool on = (Show == null || Show.Value) && cam && me && Hud.Font && island != null && island.Exists && !Hud.ModalOpen
                      && island.OnIsland(cam.transform.position, FortressIsland.IslandRadius + 60f);
            int frame = Time.frameCount;
            if (on)
                for (int i = 0; i < PlayerManager.Players.Count; i++)
                {
                    var p = PlayerManager.Players[i];
                    if (!p || p == me) continue;
                    var m = Get(p);
                    if (Place(m, p, cam)) m.Seen = frame;
                }
            foreach (var kv in _marks) if (kv.Value.Seen != frame && kv.Value.Root.gameObject.activeSelf) kv.Value.Root.gameObject.SetActive(false);
            if (Time.time >= _nextSweep) Sweep();
        }

        // The marks of players who've left.
        private void Sweep()
        {
            _nextSweep = Time.time + 2f;
            _here.Clear();
            for (int i = 0; i < PlayerManager.Players.Count; i++) if (PlayerManager.Players[i]) _here.Add(PlayerManager.Players[i].ObjectId);
            _gone.Clear();
            foreach (var kv in _marks) if (!_here.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone) { if (_marks[id].Root) Destroy(_marks[id].Root.gameObject); _marks.Remove(id); }
        }

        private Mark Get(Player p)
        {
            if (_marks.TryGetValue(p.ObjectId, out var m) && m.Root) return m;
            m = new Mark { Name = Loot.NameOf(p) };
            m.Root = Hud.Node(Hud.Root, "TeammateMark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            m.Group = m.Root.gameObject.AddComponent<CanvasGroup>();
            m.Group.blocksRaycasts = false;
            m.Group.interactable = false;
            m.Dot = Hud.Box(m.Root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13f, 13f), Mate, 3f);
            m.Dot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f); // a diamond
            m.Arrow = Hud.Icon(m.Root, "Arrow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f), Mate);
            m.Text = Hud.Label(m.Root, "", 17f, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(320f, 26f), TextAlignmentOptions.Bottom, Mate, bold: true, outline: true);
            m.Dot.enabled = false; // as Edge says until it's first placed
            _marks[p.ObjectId] = m;
            return m;
        }

        // On the screen over them (or at its edge, pointing), false when there's nothing to point at.
        private static bool Place(Mark m, Player p, Camera cam)
        {
            bool down = p.Dying && p.Dying.IsDead;
            Vector3 at;
            if (down) { var body = Body(m, p); if (!body) return false; at = body.transform.position + Vector3.up * 0.9f; }
            else if (p.Transform && p.Transform.gameObject.activeInHierarchy) at = p.Transform.position + Vector3.up * 2.25f;
            else return false;
            Vector3 vp = cam.WorldToViewportPoint(at);
            float dist = Vector3.Distance(cam.transform.position, at);
            bool edge = vp.z <= 0f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.05f || vp.y > 0.95f;
            Vector2 anchor = new Vector2(vp.x, vp.y);
            if (edge)
            {
                Vector2 d = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                if (vp.z < 0f) d = -d;
                if (d.sqrMagnitude < 1e-4f) d = Vector2.down;
                d.Normalize();
                float s = Mathf.Max(Mathf.Abs(d.x) / 0.45f, Mathf.Abs(d.y) / 0.42f);
                anchor = new Vector2(0.5f + d.x / s, 0.5f + d.y / s);
                m.Arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 180f); // the game's arrow points left
                m.Text.rectTransform.anchoredPosition = new Vector2(-d.x * 150f, -d.y * 34f); // the name in toward the middle
            }
            if (!m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(true);
            m.Root.anchorMin = m.Root.anchorMax = anchor;
            m.Root.anchoredPosition = Vector2.zero;
            if (edge != m.Edge)
            {
                m.Edge = edge;
                m.Dot.enabled = !edge;
                m.Arrow.enabled = edge;
                if (!edge) m.Text.rectTransform.anchoredPosition = new Vector2(0f, 20f);
            }
            int metres = Mathf.RoundToInt(dist);
            if (down != m.Down || (metres != m.Metres && Time.time >= m.NextText))
            {
                m.Down = down;
                m.Metres = metres;
                m.NextText = Time.time + 0.25f;
                m.Name = Loot.NameOf(p); // a friend's Steam name can come in after they do
                Color c = down ? Hud.Bad : Mate;
                m.Text.text = down ? $"{m.Name}  {L10n.T("mateDown")}  {metres}m" : $"{m.Name}  {metres}m";
                m.Text.color = c;
                m.Dot.color = c;
                m.Arrow.color = c;
            }
            float a = 1f;
            if (down) a = 0.7f + 0.3f * Mathf.Sin(Time.time * 7f);
            else if (!edge)
            {
                a = Mathf.InverseLerp(3f, 7f, dist); // right by you
                if (new Vector2(vp.x - 0.5f, vp.y - 0.5f).sqrMagnitude < 0.08f * 0.08f && dist < 30f) a = Mathf.Min(a, 0.3f); // under your crosshair
            }
            m.Group.alpha = a;
            return true;
        }

        // The body of a player who's down (what a teammate carries back): theirs as the game has it, or found among the items
        // (a friend's screen may not have been told which it is).
        private static Item Body(Mark m, Player p)
        {
            var dead = p.Dying ? p.Dying.DeadPlayer : null;
            if (dead) return dead;
            if (m.Body && m.Body.DeadPlayer && m.Body.DeadPlayer.Player == p) return m.Body;
            if (Time.time < m.NextBody) return null;
            m.NextBody = Time.time + 0.5f;
            foreach (var it in ItemManager.Items.Values)
                if (it && it.DeadPlayer && it.DeadPlayer.Player == p) { m.Body = it; return it; }
            return null;
        }
    }
}
