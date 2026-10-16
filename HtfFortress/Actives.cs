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
    // Client -> host: use my active gear. Host -> everyone: whose went off, and how long until it's ready again.
    public struct ActiveUse : IBroadcast { }
    public struct ActiveState : IBroadcast { public int PlayerId; public float Cooldown; }

    // Active gear: one slot per player, press K during a round, then it recharges. Effects run on the host.
    public class Actives : MonoBehaviour
    {
        public static Actives Instance;
        public const Key UseKey = Key.K;

        // host
        private readonly Dictionary<ulong, float> _readyAt = new Dictionary<ulong, float>();
        // client
        private float _myReadyAt, _myCooldown = 1f;
        private UnityEngine.UI.Image _panel;
        private TextMeshProUGUI _label;
        private Hud.Bar _bar;
        private NetworkManager _registeredOn;

        static Actives()
        {
            GenericWriter<ActiveUse>.SetWrite((w, v) => w.WriteBoolean(true));
            GenericReader<ActiveUse>.SetRead(r => { r.ReadBoolean(); return new ActiveUse(); });
            GenericWriter<ActiveState>.SetWrite((w, v) => { w.WriteInt32(v.PlayerId); w.WriteSingle(v.Cooldown); });
            GenericReader<ActiveState>.SetRead(r => new ActiveState { PlayerId = r.ReadInt32(), Cooldown = r.ReadSingle() });
        }

        private void Awake() => Instance = this;

        private static (Gear g, Rarity r)? Equipped(VaultData v, Player p) =>
            p && v.Personal.TryGetValue(Loot.KeyOf(p), out var l) && l.Any(x => GearCatalog.IsActive(x.Item1)) ? l.First(x => GearCatalog.IsActive(x.Item1)) : ((Gear, Rarity)?)null;

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm && nm != _registeredOn)
            {
                _registeredOn = nm;
                nm.ServerManager.RegisterBroadcast<ActiveUse>((conn, u, ch) =>
                {
                    var who = PlayerManager.Players.FirstOrDefault(p => p && p.Owner == conn);
                    if (who) ServerUse(who);
                });
                nm.ClientManager.RegisterBroadcast<ActiveState>((s, ch) =>
                {
                    if (!Player.LocalPlayer || s.PlayerId != Player.LocalPlayer.ObjectId) return;
                    _myCooldown = Mathf.Max(0.1f, s.Cooldown);
                    _myReadyAt = Time.time + s.Cooldown;
                });
            }
            var kb = Keyboard.current;
            if (kb != null && kb[UseKey].wasPressedThisFrame && !ChatManager.IsTyping && !Hud.ModalOpen && Siege.Instance.ViewActive && InstanceFinder.IsClientStarted)
                InstanceFinder.ClientManager.Broadcast(new ActiveUse());
            DrawHud();
        }

        // ---------- host ----------

        public bool ServerUse(Player who)
        {
            if (!Siege.Instance.ServerActive || !who || !who.Transform || !PlayerManager.AlivePlayers.Contains(who)) return false;
            var eq = Equipped(Loot.Instance.Vault, who);
            if (eq == null) return false;
            var (g, r) = eq.Value;
            ulong key = Loot.KeyOf(who);
            if (_readyAt.TryGetValue(key, out float at) && Time.time < at) return false;
            float v = GearCatalog.Value(g, r);
            Vector3 pos = who.Transform.position, fwd = who.Transform.forward; fwd.y = 0f;
            switch (g)
            {
                case Gear.Overclock:
                    Turrets.OverclockUntil = Time.time + v;
                    Fx.Announce("activeOverclock", Mathf.RoundToInt(v));
                    break;
                case Gear.FieldRepair:
                    foreach (var p in BuildSystem.Instance.ServerRepairArea(pos, 10f, v)) Fx.Send(FxKind.Heal, pos + Vector3.up, p + Vector3.up * 0.5f);
                    Fx.Send(FxKind.Sell, pos, pos);
                    break;
                case Gear.Decoy:
                    Siege.Instance.ServerDecoy(pos + (fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward) * 2.5f, v);
                    break;
                case Gear.NetCannon:
                    FortressNet.Instance.ServerScoopAround(who, v);
                    break;
                case Gear.Shockwave:
                    Siege.Instance.ServerShockwave(pos, 6f, v, 20);
                    break;
            }
            float cd = GearCatalog.Cooldown(g, r);
            _readyAt[key] = Time.time + cd;
            InstanceFinder.ServerManager.Broadcast(new ActiveState { PlayerId = who.ObjectId, Cooldown = cd });
            AutoTest.Count("active_" + g);
            return true;
        }

        public void DebugResetCooldown(Player who) => _readyAt.Remove(Loot.KeyOf(who));

        // ---------- client: the active slot, above the net ----------

        private void DrawHud()
        {
            var eq = Player.LocalPlayer && Loot.Instance ? Equipped(Loot.Instance.ClientVault, Player.LocalPlayer) : null;
            bool show = eq != null && Siege.Instance && Siege.Instance.ViewActive && Hud.Font;
            if (!show) { if (_panel) _panel.gameObject.SetActive(false); return; }
            if (!_panel)
            {
                _panel = Hud.Box(Hud.Root, new Vector2(1f, 0f), new Vector2(-24f, 236f), new Vector2(320f, 62f), Hud.Panel, 16f);
                _label = Hud.Label(_panel.transform, "", 21f, new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(300f, 30f), TextAlignmentOptions.TopLeft, pivot: new Vector2(0f, 1f));
                _bar = Hud.Bar.Make(_panel.transform, new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(292f, 10f), Hud.Accent);
            }
            _panel.gameObject.SetActive(true);
            var (g, r) = eq.Value;
            float left = _myReadyAt - Time.time;
            bool ready = left <= 0f;
            string state = ready ? $"<color={Hud.Hex(Hud.Good)}>{L10n.T("activeReady")}</color>" : $"<color={Hud.Hex(Hud.Muted)}>{Mathf.CeilToInt(left)}s</color>";
            _label.text = Hud.Keys($"[K] <color=#{GearCatalog.Hex(r)}>{GearCatalog.Name(g)}</color>  {state}");
            _bar.Set(ready ? 1f : 1f - left / _myCooldown, ready ? Hud.Good : Hud.Accent);
        }
    }
}
