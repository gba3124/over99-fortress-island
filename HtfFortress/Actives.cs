using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Serializing;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HtfFortress
{
    // Client -> host: use this active gear. Host -> everyone: whose went off, and how long until it's ready again.
    public struct ActiveUse : IBroadcast { public Gear Gear; }
    public struct ActiveState : IBroadcast { public int PlayerId; public float Cooldown; }

    // Active gear: nothing to wear (Owen: "不用去裝…每個人都可以切換k那邊的道具看要不要用 這樣才及時主動"). Every kind the
    // team has in the vault, at its best colour, is everyone's: K uses the one you've picked, Shift+K picks the next. Each
    // is used up (Owen: "k的技能那些要是耗材"), the best colour first, so they don't merge by themselves any more (three
    // uses would become one); the smith's bench still forges three into one. A moment between two (Lock) so one press
    // doesn't spend two. During a round only; the effects run on the host.
    public class Actives : MonoBehaviour
    {
        public static Actives Instance;
        public const Key UseKey = Key.K;
        public const float Lock = 3f;

        // host
        private readonly Dictionary<ulong, float> _readyAt = new Dictionary<ulong, float>();
        // client
        private float _myReadyAt, _myCooldown = 1f;
        private (int at, int count, Gear g, Rarity r, int uses, int secs) _shown;
        private float _relabelAt;
        private static readonly List<(Gear g, Rarity r, int stars)> NoneOwned = new List<(Gear g, Rarity r, int stars)>(); // read only
        private static Gear? _picked;
        private UnityEngine.UI.Image _panel, _pic;
        private TextMeshProUGUI _label, _count;
        private Hud.Bar _bar;
        private NetworkManager _registeredOn;

        static Actives()
        {
            GenericWriter<ActiveUse>.SetWrite((w, v) => w.WriteByte((byte)v.Gear));
            GenericReader<ActiveUse>.SetRead(r => new ActiveUse { Gear = (Gear)r.ReadByte() });
            GenericWriter<ActiveState>.SetWrite((w, v) => { w.WriteInt32(v.PlayerId); w.WriteSingle(v.Cooldown); });
            GenericReader<ActiveState>.SetRead(r => new ActiveState { PlayerId = r.ReadInt32(), Cooldown = r.ReadSingle() });
        }

        private void Awake() => Instance = this;
        public bool ClientHasActive => Loot.Instance && Owned(Loot.Instance.ClientVault).Count > 0;

        // The active gear the team has, each kind at its best colour.
        public static List<(Gear g, Rarity r, int stars)> Owned(VaultData v)
        {
            var list = new List<(Gear, Rarity, int)>();
            foreach (var g in GearCatalog.All)
            {
                if (!GearCatalog.IsActive(g) || g == Gear.NetCannon) continue;
                for (int r = (int)Rarity.Legendary; r >= 0; r--) // an int: a Rarity is a byte, and Common-- wraps to 255 (that hung the game)
                {
                    int st = 0;
                    for (int s = GearCatalog.MaxStars; s >= 0; s--) if (v.Count(g, (Rarity)r, s) > 0) { st = s; break; }
                    if (v.Count(g, (Rarity)r) > 0 || AnyStars(v, g, (Rarity)r)) { list.Add((g, (Rarity)r, st)); break; }
                }
            }
            return list;
        }

        // Loops rather than LINQ here: the HUD and the loadout bar ask a few times a frame on every screen, and each lambda
        // over g and r was a new closure (a few kB of garbage a frame with nothing owned).
        private static bool AnyStars(VaultData v, Gear g, Rarity r)
        {
            foreach (var k in v.Owned.Keys) if (k.Item1 == g && k.Item2 == r) return true;
            return false;
        }

        // Which of them K uses now: the one picked, while the team still has it; else the first.
        private static int PickedIndex(List<(Gear g, Rarity r, int stars)> owned)
        {
            if (_picked is Gear p) for (int i = 0; i < owned.Count; i++) if (owned[i].g == p) return i;
            return 0;
        }

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ServerManager.RegisterBroadcast(Net.Safe<ActiveUse>((conn, u, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) ServerUse(who, u.Gear);
                }));
                nm.ClientManager.RegisterBroadcast<ActiveState>((s, ch) =>
                {
                    if (!Player.LocalPlayer || s.PlayerId != Player.LocalPlayer.ObjectId) return;
                    _myCooldown = Mathf.Max(0.1f, s.Cooldown);
                    _myReadyAt = Time.time + s.Cooldown;
                });
            }
            var kb = Keyboard.current;
            if (kb != null && kb[UseKey].wasPressedThisFrame && !ChatManager.IsTyping && !Hud.ModalOpen && InstanceFinder.IsClientStarted)
            {
                // Say why nothing happens (the playtest pressed K with nothing worn, and outside a round).
                var owned = Owned(Loot.Instance.ClientVault);
                if (owned.Count == 0) Hud.Banner(Hud.Keys(L10n.T("activeNone")), 2.5f);
                else if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
                {
                    int i = (PickedIndex(owned) + 1) % owned.Count;
                    _picked = owned[i].g;
                    Hud.Banner(Hud.Keys(L10n.T("activePicked", GearCatalog.Label(owned[i].g, owned[i].r) + (owned[i].stars > 0 ? " " + new string('★', owned[i].stars) : ""), i + 1, owned.Count)), 1.5f);
                    Fx.Sound("EquipItem", Player.LocalPlayer ? Player.LocalPlayer.Transform.position : Vector3.zero, 0.6f, AudioDistance.Short);
                }
                else if (!Siege.Instance.ViewActive) Hud.Banner(L10n.T("activeRoundOnly"), 2f);
                else InstanceFinder.ClientManager.Broadcast(new ActiveUse { Gear = owned[PickedIndex(owned)].g });
            }
            DrawHud();
        }

        // ---------- host ----------

        public bool ServerUse(Player who, Gear g)
        {
            if (!Siege.Instance.ServerActive || !who || !who.Transform || !PlayerManager.AlivePlayers.Contains(who)) return false;
            int k = Owned(Loot.Instance.Vault).FindIndex(x => x.g == g);
            if (k < 0) return false; // the team doesn't have it (any more)
            var r = Owned(Loot.Instance.Vault)[k].r;
            int stars = Owned(Loot.Instance.Vault)[k].stars;
            ulong key = Loot.KeyOf(who);
            if (_readyAt.TryGetValue(key, out float at) && Time.time < at) return false;
            float v = Loot.Instance.Vault.ValueOf(g, r, stars); // enhanced legendaries and stars hit harder
            Vector3 pos = who.Transform.position, fwd = who.Transform.forward; fwd.y = 0f;
            switch (g)
            {
                case Gear.Overclock:
                    Turrets.OverclockUntil = Mathf.Max(Turrets.OverclockUntil, Time.time + v); // never cuts a teammate's longer one short
                    Fx.Announce("activeOverclock", Mathf.RoundToInt(v));
                    break;
                case Gear.FieldRepair:
                    var mended = BuildSystem.Instance.ServerRepairArea(pos, 10f, v);
                    // The lighthouse too when it's in reach, at half the share (it's worth more than any wall).
                    if ((Siege.Instance.CorePos - pos).Flat().magnitude <= 12f && Siege.Instance.ServerHealCore(v * 0.5f) > 0) mended.Add(Siege.Instance.CorePos + Vector3.up * 2f);
                    if (mended.Count == 0) return false; // nothing hurt in reach: it stays ready
                    foreach (var p in mended) Fx.Send(FxKind.Heal, pos + Vector3.up, p + Vector3.up * 0.5f);
                    Fx.Send(FxKind.Sell, pos, pos);
                    break;
                case Gear.Decoy:
                    Siege.Instance.ServerDecoy(pos + (fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward) * 2.5f, v);
                    break;
                case Gear.NetCannon: return false; // the hand net is gone: this active is retired
                case Gear.Shockwave:
                    // 40 damage grows with the wave's health, so it still matters at wave 15 (a flat 40 is nothing to an 800 HP fish).
                    if (Siege.Instance.ServerShockwave(pos, 6f, v, Mathf.RoundToInt(40f * FortressPlugin.Instance.WaveHpMult), who) == 0) return false; // nobody in reach: it stays ready
                    break;
            }
            Loot.Instance.Vault.AddStar(g, r, stars, -1); // used up
            Loot.Instance.SaveVault();
            _readyAt[key] = Time.time + Lock;
            InstanceFinder.ServerManager.Broadcast(new ActiveState { PlayerId = who.ObjectId, Cooldown = Lock });
            AutoTest.Count("active_" + g);
            return true;
        }

        public void DebugResetCooldown(Player who) => _readyAt.Remove(Loot.KeyOf(who));

        // ---------- client: the picked active, above the net ----------

        private void DrawHud()
        {
            var owned = Player.LocalPlayer && Loot.Instance ? Owned(Loot.Instance.ClientVault) : NoneOwned;
            bool show = owned.Count > 0 && Siege.Instance && Siege.Instance.ViewActive && Hud.Font;
            if (!show) { if (_panel) _panel.gameObject.SetActive(false); return; }
            if (!_panel)
            {
                _panel = Hud.Box(Hud.Root, new Vector2(1f, 0f), new Vector2(-24f, 264f), new Vector2(320f, 62f), Hud.Panel, 16f); // above the net and its hint line
                _pic = Hud.PictureSlot(_panel.transform, new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(52f, 52f));
                _label = Hud.Label(_panel.transform, "", 21f, new Vector2(0f, 1f), new Vector2(64f, -8f), new Vector2(200f, 30f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
                _label.enableWordWrapping = false; _label.enableAutoSizing = true; _label.fontSizeMin = 14f; _label.fontSizeMax = 21f; // one line, whatever the name
                _count = Hud.Label(_panel.transform, "", 16f, new Vector2(1f, 1f), new Vector2(-10f, -10f), new Vector2(50f, 26f), TextAlignmentOptions.TopRight, Hud.Muted, pivot: new Vector2(1f, 1f));
                _bar = Hud.Bar.Make(_panel.transform, new Vector2(0f, 0f), new Vector2(64f, 12f), new Vector2(242f, 10f), Hud.Accent);
                _relabelAt = 0f;
            }
            _panel.gameObject.SetActive(true);
            int at = PickedIndex(owned);
            var (g, r, st) = owned[at];
            float left = _myReadyAt - Time.time;
            bool ready = left <= 0f;
            int uses = 0; // what's left of it, every colour
            foreach (var x in GearCatalog.Rarities) uses += Loot.Instance.ClientVault.Count(g, x);
            // The texts are rebuilt when what they say changes (and twice a second anyway, for a language switch), not as new
            // strings every frame on every screen.
            var shown = (at, owned.Count, g, r, uses, ready ? 0 : Mathf.CeilToInt(left));
            if (!shown.Equals(_shown) || Time.time >= _relabelAt)
            {
                _shown = shown; _relabelAt = Time.time + 0.5f;
                _count.text = owned.Count > 1 ? $"{at + 1}/{owned.Count}" : ""; // Shift+K for the others
                Hud.ShowPicture(_pic, "Gear" + g);
                string state = ready ? $"<color={Hud.Hex(Hud.Good)}>{L10n.T("activeReady")}</color>" : $"<color={Hud.Hex(Hud.Muted)}>{Mathf.CeilToInt(left)}s</color>";
                _label.text = Hud.Keys($"[K] <color=#{GearCatalog.Hex(r)}>{GearCatalog.Name(g)}</color> ×{uses}  {state}");
            }
            _bar.Set(ready ? 1f : 1f - left / _myCooldown, ready ? Hud.Good : Hud.Accent);
        }
    }
}
