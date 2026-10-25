using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HtfFortress
{
    // Host-side per-player numbers for the results screen: who killed what (by hand or with the turrets they built),
    // who fished, who built, who went down. Sent to everyone as a compact table inside RoundResult.
    public static class Stats
    {
        public class Row { public string Name; public int Kills, TurretKills, Fished, Built, Downs; }

        private static readonly Dictionary<int, Row> Rows = new Dictionary<int, Row>();
        private static readonly Dictionary<Creature, (int player, float time)> LastHand = new Dictionary<Creature, (int, float)>();
        private static readonly Dictionary<Creature, int> LastTurretOwner = new Dictionary<Creature, int>();
        private static readonly HashSet<int> WasAlive = new HashSet<int>();

        public static void Reset()
        {
            Rows.Clear(); LastHand.Clear(); LastTurretOwner.Clear(); WasAlive.Clear();
            foreach (var p in PlayerManager.Players) if (p) RowOf(p.ObjectId);
        }

        private static Row RowOf(int playerId)
        {
            if (!Rows.TryGetValue(playerId, out var r))
            {
                var p = PlayerManager.Players.FirstOrDefault(x => x && x.ObjectId == playerId);
                Rows[playerId] = r = new Row { Name = p ? (string.IsNullOrEmpty(p.SteamName) ? "Player " + playerId : p.SteamName) : "Player " + playerId };
            }
            return r;
        }

        public static void HandHit(Creature c, Player p) { if (c && p) LastHand[c] = (p.ObjectId, Time.time); }
        public static void TurretHit(Creature c, int ownerId) { if (c && ownerId >= 0) LastTurretOwner[c] = ownerId; }
        public static void Built(int ownerId) { if (ownerId >= 0) RowOf(ownerId).Built++; }
        public static void Fished(int playerId, int coins) => RowOf(playerId).Fished += coins;

        // A kill goes to the last player who hit it by hand in the last 4 s, else to whoever built the turret that hit it.
        // Returns the credited player's id (-1 if nobody).
        public static int Killed(Creature c)
        {
            int who = -1;
            if (LastHand.TryGetValue(c, out var h) && Time.time - h.time < 4f) RowOf(who = h.player).Kills++;
            else if (LastTurretOwner.TryGetValue(c, out int owner)) RowOf(who = owner).TurretKills++;
            LastHand.Remove(c); LastTurretOwner.Remove(c);
            return who;
        }

        // Called every frame during a round: count each time someone goes down.
        public static void TrackDowns()
        {
            foreach (var p in PlayerManager.Players)
            {
                if (!p) continue;
                bool alive = PlayerManager.AlivePlayers.Contains(p);
                if (alive) WasAlive.Add(p.ObjectId);
                else if (WasAlive.Remove(p.ObjectId)) { RowOf(p.ObjectId).Downs++; AutoTest.Count("player_downed"); }
            }
        }

        // "name|kills|turretKills|fished|built|downs" per line.
        public static string Table() =>
            string.Join("\n", Rows.Values.Select(r => $"{r.Name.Replace("|", "/").Replace("\n", " ")}|{r.Kills}|{r.TurretKills}|{r.Fished}|{r.Built}|{r.Downs}"));

        public static List<Row> Parse(string table)
        {
            var rows = new List<Row>();
            foreach (var line in (table ?? "").Split('\n'))
            {
                var f = line.Split('|');
                if (f.Length < 6) continue;
                int.TryParse(f[1], out int k); int.TryParse(f[2], out int tk); int.TryParse(f[3], out int fi); int.TryParse(f[4], out int b); int.TryParse(f[5], out int d);
                rows.Add(new Row { Name = f[0], Kills = k, TurretKills = tk, Fished = fi, Built = b, Downs = d });
            }
            return rows;
        }
    }

    // Every player weapon hit on a creature (guns, bow, knife, fists, disc) lands here on the host: remember who hit last.
    [HarmonyPatch(typeof(Server), "RpcLogic___HitCreature___215526726")]
    internal static class RecordHandHits
    {
        private static void Prefix(Creature __0, Player __1, ref int __2)
        {
            Stats.HandHit(__0, __1);
            if (__0 && Enemies.Instance && Enemies.Instance.TryGet(__0, out _))
            {
                var sv = Loot.Instance ? Loot.Instance.Vault : null;
                float mult = FortressPlugin.HandDamage.Value * (sv != null ? sv.DamageMult : 1f)
                           * (1f + Perks.Of(__1, Gear.Gauntlet)) * (Siege.Instance.IsBubbled(__0) ? 1.5f : 1f);
                if (sv != null && sv.CritChance > 0f && Random.value < sv.CritChance) mult *= 2f; // the keeper's crit also covers your own gun
                // One of the game's guns as a fortress weapon: by its rarity and the wave; a legendary now and then hits three times as hard.
                if (WeaponItems.Held(__1, out var g, out var r) && GearCatalog.IsGameGun(g))
                {
                    mult *= WeaponItems.GunDamage(g, r) * (1f + 0.08f * WeaponItems.StarsOf(__1.Holding ? __1.Holding.HeldItem : null));
                    if (r == Rarity.Legendary && Random.value < WeaponItems.GunCrit) { mult *= 3f; Perks.Proc(g, __0.transform.position); }
                }
                __2 = Mathf.Max(1, Mathf.RoundToInt(__2 * mult));
                // Legendary: each punch stuns. Melee only (fists, knife, brass knuckles): on an SMG's 20 hits a second it would hold any fish, bosses too, still for good.
                if (Perks.Legend(__1, Gear.Gauntlet) && IsMelee(__1)) { Siege.Instance.Slow(__0, 1f, 0.6f); Perks.Proc(Gear.Gauntlet, __0.transform.position); }
            }
        }

        private static bool IsMelee(Player p)
        {
            var held = p && p.Holding ? p.Holding.HeldItem : null;
            return !held || held is Melee;
        }
    }

    // Dynamite hurts fish through its own path (no hit RPC): credit the thrower for fortress fish in the blast, so the kill,
    // the stats and the thrower's lucky charm count.
    [HarmonyPatch(typeof(ExplosionManager), nameof(ExplosionManager.ServerExplode))]
    internal static class CreditExplosions
    {
        private static void Prefix(Item item, ExplosionInfo info)
        {
            if (!item || info == null || !Siege.Instance || !Siege.Instance.ServerActive) return;
            Player p = item.Explosive && item.Explosive.PlayerWhoForcedExplosion ? item.Explosive.PlayerWhoForcedExplosion : item.LastPlayer;
            if (!p) return;
            float r2 = info.DamageRadius * info.DamageRadius;
            foreach (var c in FortressPlugin.Alive)
                if (c && (c.transform.position - item.transform.position).sqrMagnitude <= r2) Stats.HandHit(c, p);
        }
    }

    // Each hand kill queues the game's 2-3 s kill-score popup; thirty kills in a wave would keep playing into the next one.
    // During a round at most one waits.
    [HarmonyPatch(typeof(PlayerKillScore), nameof(PlayerKillScore.AddKillScore))]
    internal static class FewerKillScorePopups
    {
        private static System.Reflection.FieldInfo _queue;

        private static bool Prefix(PlayerKillScore __instance)
        {
            if (!(Siege.Instance && Siege.Instance.ViewActive)) return true;
            _queue = _queue ?? AccessTools.Field(typeof(PlayerKillScore), "_killScoreQueue");
            return !(_queue?.GetValue(__instance) is System.Collections.ICollection waiting && waiting.Count > 0);
        }
    }
}
