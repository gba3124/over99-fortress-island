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
        public static void Killed(Creature c)
        {
            if (LastHand.TryGetValue(c, out var h) && Time.time - h.time < 4f) RowOf(h.player).Kills++;
            else if (LastTurretOwner.TryGetValue(c, out int owner)) RowOf(owner).TurretKills++;
            LastHand.Remove(c); LastTurretOwner.Remove(c);
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

    // Player weapon hits on creatures land here on the host: remember who hit last.
    [HarmonyPatch(typeof(Server), "RpcLogic___HitCreature___215526726")]
    internal static class RecordHandHits
    {
        private static void Prefix(Creature __0, Player __1) => Stats.HandHit(__0, __1);
    }
}
