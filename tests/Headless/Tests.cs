using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FishNet.Broadcast;
using FishNet.Serializing;
using UnityEngine;

namespace HtfFortress.Headless
{
    // Pass / fail checks on the mod's real code. `dotnet run -- sim` also prints the balance simulations (Sim.cs).
    public static class Tests
    {
        private static int _pass, _fail;
        private static string _section;

        public static void Check(bool ok, string what)
        {
            if (ok) { _pass++; return; }
            _fail++;
            Console.WriteLine($"  FAIL [{_section}] {what}");
        }

        private static void Section(string name) { _section = name; Console.WriteLine("== " + name); }

        private static string Src => Path.Combine(Program.Repo, "HtfFortress");
        private static IEnumerable<string> Sources => Directory.GetFiles(Src, "*.cs");

        public static int Run(string[] args)
        {
            foreach (var t in typeof(Loot).Assembly.GetTypes())
                try { RuntimeHelpers.RunClassConstructor(t.TypeHandle); }
                catch (Exception e) { _fail++; Console.WriteLine($"  FAIL static init {t.Name}: {e.GetBaseException().Message}"); }

            Localization();
            Network();
            Vault();
            Buildings();
            EnemyTable();
            LootRolls();
            Waves();
            WeaponChecks();
            HelperChecks();
            Performance();
            PlayerChecks();
            Patches();
            IslandLayout();
            Console.WriteLine($"\n{_pass} checks passed, {_fail} failed");
            if (args.Contains("sim")) Sim.All();
            else if (args.Contains("prices")) Sim.ModPrices(); // just the weapon mods' prices against what a round brings in
            else if (args.Contains("dps")) Sim.DpsTable(); // just the weapons' damage a second
            if (args.Contains("variants")) Sim.Variants();
            return _fail == 0 ? 0 : 1;
        }

        // ---------- every string in three languages, every key the code asks for, every {n} it fills ----------

        public static Dictionary<string, string[]> Table =>
            (Dictionary<string, string[]>)typeof(L10n).GetField("Table", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");
        private static int Need(string s) => Placeholder.Matches(s).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();

        private static void Localization()
        {
            Section("localization");
            var table = Table;
            foreach (var kv in table)
            {
                Check(kv.Value.Length == 3 && kv.Value.All(s => !string.IsNullOrWhiteSpace(s)), $"'{kv.Key}' needs English, 繁中 and 简中");
                if (kv.Value.Length != 3) continue;
                var sets = kv.Value.Select(s => string.Join(",", Placeholder.Matches(s).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x))).ToArray();
                Check(sets.Distinct().Count() == 1, $"'{kv.Key}' placeholders differ between languages: {string.Join(" | ", sets)}");
                foreach (var s in kv.Value)
                {
                    try { string.Format(s, Enumerable.Range(0, Need(s)).Select(i => (object)i).ToArray()); }
                    catch (FormatException) { Check(false, $"'{kv.Key}' is not a valid format string: {s}"); }
                }
            }

            // Each key once in L10n.cs: the table's index initialiser takes the last one silently, so a second entry for a key
            // used elsewhere quietly changed the first one's text.
            var dupes = new Regex(@"\[""([A-Za-z0-9_]+)""\]\s*=\s*new\[\]").Matches(File.ReadAllText(Sources.First(f => Path.GetFileName(f) == "L10n.cs")))
                .Cast<Match>().GroupBy(m => m.Groups[1].Value).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check(dupes.Count == 0, "keys written twice in L10n.cs: " + string.Join(", ", dupes));

            // Literal keys in the code, and how many values each call passes (a {n} without its value throws in the middle of a frame).
            var calls = new List<(string key, int args, bool announce, string where)>();
            foreach (var file in Sources)
            {
                string code = File.ReadAllText(file);
                // Only a whole literal key ("gear" + g is built at run time and checked below).
                foreach (Match m in new Regex(@"(L10n\.T|Announce|Shout)\(\s*""([^""]+)""\s*(?=[,)])").Matches(code))
                    calls.Add((m.Groups[2].Value, CountArgs(code, m.Index + m.Length), m.Groups[1].Value != "L10n.T",
                               Path.GetFileName(file) + ":" + (code.Take(m.Index).Count(c => c == '\n') + 1)));
            }
            foreach (var (key, args, announce, where) in calls)
            {
                if (!table.TryGetValue(key, out var row)) { Check(false, $"missing key '{key}' ({where})"); continue; }
                int need = row.Max(Need);
                // L10n.T(key) with no values shows "{0}" as is; Announce always formats with its one number.
                Check(announce ? need <= 1 : need <= args, $"'{key}' needs {need} values, {where} passes {(announce ? 1 : args)}");
            }

            // Keys built at run time.
            var dynamicKeys = new List<string>();
            foreach (Gear g in Enum.GetValues(typeof(Gear)))
            {
                dynamicKeys.Add("gear" + g); dynamicKeys.Add("gearFx" + g);
                if (!GearCatalog.IsActive(g)) { dynamicKeys.Add("gearLegend" + g); dynamicKeys.Add("legendShort" + g); }
            }
            foreach (Rarity r in Enum.GetValues(typeof(Rarity))) dynamicKeys.Add("rarity" + r);
            foreach (EnemyKind k in Enum.GetValues(typeof(EnemyKind)))
            {
                dynamicKeys.Add("enemy" + k);
                if (EnemyCatalog.IsBoss(k)) foreach (var what in new[] { "bossName", "bossHint", "bossEpithet", "bossQuote", "trophy", "trophyDropped", "tip_foe" }) dynamicKeys.Add(what + k);
            }
            foreach (PieceType t in Enum.GetValues(typeof(PieceType)))
            {
                dynamicKeys.Add(t.ToString());
                if (Catalog.HasBranches(t)) for (int b = 1; b <= 2; b++) { dynamicKeys.Add(Catalog.BranchKey(t, b)); dynamicKeys.Add(Catalog.BranchKey(t, b) + "_d"); }
            }
            dynamicKeys.Add("WallStone"); dynamicKeys.Add("WallMetal");
            var items = (string[])typeof(Loot).GetField("GameItems", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            dynamicKeys.AddRange(items.Select(n => "item_" + n));
            foreach (var g in GearCatalog.WeaponKinds) dynamicKeys.Add("glyph" + g);
            foreach (var k in EnemyCatalog.BossOrder) { dynamicKeys.Add("absMove" + k); dynamicKeys.Add("absMoveFx" + k); } // the absorber's trophy moves
            // The smithy's mod lines: every key WeaponMods picks between, and each fortress weapon's trait.
            dynamicKeys.AddRange(new Regex(@"""(mod[A-Z]\w*)""").Matches(File.ReadAllText(Path.Combine(Src, "WeaponMods.cs"))).Cast<Match>().Select(m => m.Groups[1].Value));
            foreach (var g in GearCatalog.WeaponKinds.Where(g => !GearCatalog.IsGameGun(g))) dynamicKeys.Add("modTrait" + g);
            // Tip cards: every rule id in Tips.cs, plus "foe" + each special kind.
            string tips = File.ReadAllText(Path.Combine(Src, "Tips.cs"));
            var ids = new Regex(@"\(""(\w+)"", \(\) =>").Matches(tips).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            string foes = tips.Substring(tips.IndexOf("foreach (var k in new[]"));
            foes = foes.Substring(0, foes.IndexOf("})"));
            ids.AddRange(new Regex(@"EnemyKind\.(\w+)").Matches(foes).Cast<Match>().Select(m => "foe" + m.Groups[1].Value));
            Check(ids.Count >= 15, $"found {ids.Count} tip rules in Tips.cs");
            foreach (var id in ids) { dynamicKeys.Add("tip_" + id); dynamicKeys.Add("tip_" + id + "_t"); }
            // The help panel: every "help…" key Tips.cs names, a line for each kind of fish it shows, and every picture there.
            dynamicKeys.AddRange(new Regex(@"""(help[A-Z]\w*)""(?!\s*\+)").Matches(tips).Cast<Match>().Select(m => m.Groups[1].Value));
            string shown = tips.Substring(tips.IndexOf("Foes ="));
            shown = shown.Substring(0, shown.IndexOf("};"));
            var shownKinds = new Regex(@"EnemyKind\.(\w+)").Matches(shown).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Check(shownKinds.Count >= 8, $"the help shows {shownKinds.Count} kinds of fish");
            dynamicKeys.AddRange(shownKinds.Select(k => "helpFoe" + k));
            var pics = new Regex(@"\(""(\w+)"", ""help\w+T"", ""help\w+""\)").Matches(tips).Cast<Match>().Select(m => m.Groups[1].Value)
                .Concat(shownKinds.Select(k => "HelpFoe" + k)).Append("HelpBoss").ToList();
            Check(pics.Count >= 20, $"found {pics.Count} help pictures");
            foreach (var pic in pics) Check(File.Exists(Path.Combine(Src, "Icons", pic + ".png")), $"help picture Icons/{pic}.png is missing");
            // Each player's settings (ClientSettings.Add): its name, and the line under it.
            foreach (var file in Sources)
                foreach (Match m in new Regex(@"Add\(\s*""(set[A-Z]\w*)""").Matches(File.ReadAllText(file))) { dynamicKeys.Add(m.Groups[1].Value); dynamicKeys.Add(m.Groups[1].Value + "D"); }
            foreach (var k in dynamicKeys.Distinct()) Check(table.ContainsKey(k), $"missing key '{k}' (built at run time)");
            Console.WriteLine($"  {table.Count} strings, {calls.Count} literal uses, {dynamicKeys.Distinct().Count()} run-time keys");
        }

        // Counts the top-level arguments after the first one, from just past the key literal to the closing parenthesis.
        private static int CountArgs(string code, int i)
        {
            int depth = 1, args = 0;
            bool str = false;
            for (; i < code.Length && depth > 0; i++)
            {
                char c = code[i];
                if (str) { if (c == '\\') i++; else if (c == '"') str = false; continue; }
                if (c == '"') str = true;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 1) args++;
            }
            return args;
        }

        // ---------- every broadcast: registered, and what goes out comes back field for field ----------

        private static void Network()
        {
            Section("network messages");
            var rng = new System.Random(7);
            var types = typeof(Loot).Assembly.GetTypes().Where(t => t.IsValueType && typeof(IBroadcast).IsAssignableFrom(t)).ToList();
            foreach (var t in types)
            {
                var writer = typeof(GenericWriter<>).MakeGenericType(t).GetProperty("Write").GetValue(null) as Delegate;
                var reader = typeof(GenericReader<>).MakeGenericType(t).GetProperty("Read").GetValue(null) as Delegate;
                Check(writer != null && reader != null, $"{t.Name} has no hand-registered serializer (FishNet would fail to send it)");
                if (writer == null || reader == null) continue;
                for (int round = 0; round < 20; round++)
                {
                    object sample = Activator.CreateInstance(t);
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) f.SetValue(sample, RandomValue(f.FieldType, rng));
                    var w = new Writer();
                    writer.DynamicInvoke(w, sample);
                    var r = new Reader(w.GetArraySegment(), null);
                    object back = reader.DynamicInvoke(r);
                    Check(r.Remaining == 0, $"{t.Name}: reader left {r.Remaining} bytes unread (write/read out of step)");
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        if (!SameValue(f.GetValue(sample), f.GetValue(back)))
                        {
                            Check(false, $"{t.Name}.{f.Name} does not survive the wire: sent {f.GetValue(sample)}, got {f.GetValue(back)}");
                            break;
                        }
                }
            }
            Console.WriteLine($"  {types.Count} message types round-tripped");
            // Two different builds find each other through this one: its name (FishNet's key) and layout must never change.
            var hello = typeof(ModHello).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Check(typeof(ModHello).FullName == "HtfFortress.ModHello" && hello.Length == 1 && hello[0].FieldType == typeof(string),
                  "ModHello changed: builds before and after the change can no longer tell each other apart");
            // The id players compare is the start of the DLL's SHA-256, the same digits tools/pack.sh puts in the zip name.
            string dllHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(VersionCheck).Assembly.Location))).ToLowerInvariant();
            Check(VersionCheck.Build == FortressPlugin.Version + "-" + dllHash.Substring(0, 6), $"build id {VersionCheck.Build} isn't the DLL's own hash ({dllHash.Substring(0, 6)})");
        }

        private static bool SameValue(object a, object b)
        {
            if (a is Array aa && b is Array bb)
            {
                if (aa.Length != bb.Length) return false;
                for (int i = 0; i < aa.Length; i++) if (!SameValue(aa.GetValue(i), bb.GetValue(i))) return false;
                return true;
            }
            return Equals(a, b);
        }

        private static object RandomValue(Type t, System.Random rng)
        {
            if (t == typeof(bool)) return rng.Next(2) == 1;
            if (t == typeof(int)) return rng.Next(-100000, 100000);
            if (t == typeof(byte)) return (byte)rng.Next(256);
            if (t == typeof(ulong)) return (ulong)rng.Next() << 20;
            if (t == typeof(float)) return (float)Math.Round(rng.NextDouble() * 200 - 100, 3);
            if (t == typeof(string)) return new[] { "", "a b", "王 小明", "own Scope Epic 2\nfort Rebar Legendary", "x|y" }[rng.Next(5)];
            if (t == typeof(Vector3)) return new Vector3(rng.Next(-500, 500) / 4f, rng.Next(-50, 50) / 4f, rng.Next(-500, 500) / 4f);
            if (t.IsEnum) { var vals = Enum.GetValues(t); return vals.GetValue(rng.Next(vals.Length)); }
            if (t.IsArray)
            {
                var el = t.GetElementType();
                int n = rng.Next(0, 4);
                var arr = Array.CreateInstance(el, n);
                for (int i = 0; i < n; i++) arr.SetValue(RandomValue(el, rng), i);
                return arr;
            }
            // Any other struct of ours (a message inside a batch): fill its fields.
            if (t.IsValueType && t.Assembly == typeof(Loot).Assembly)
            {
                var o = Activator.CreateInstance(t);
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) f.SetValue(o, RandomValue(f.FieldType, rng));
                return o;
            }
            throw new Exception("no sample for " + t);
        }

        // ---------- the vault: save file, and every button under random clicking ----------

        public static int Items(VaultData v) => v.Owned.Values.Sum() + v.Fortress.Count + v.Personal.Values.Sum(l => l.Count);

        private static void Vault()
        {
            Section("vault");
            // Expansions: they cost scales, wait for the record, add a slot each, stop at the top, survive a save.
            {
                var e = new VaultData { Scales = 3000, Best = 0 };
                Check(e.SlotsOf(0) == 4 && e.SlotsOf(1) == 2 && e.StorageSlots == 40, "starting slots and room");
                Check(!e.Apply(VaultAction.Expand, default, default, 3, 1), "an expansion that isn't one (the old weapon rack) is refused");
                // Expansions are gated by scales only now (Owen: "倉庫格子的限制 只用魚鱗限制就好 不要用關卡"): no record needed.
                int bought = 0;
                while (e.Apply(VaultAction.Expand, default, default, (int)Expansion.TeamSlot, 1)) bought++;
                Check(bought == 6 && e.SlotsOf(0) == 10, $"team slots: bought {bought}, now {e.SlotsOf(0)} (expect 6 more, 10)");
                Check(e.Scales == 3000 - GearCatalog.TeamSlotPrices.Sum(p => p.scales), "team slots charged their price");
                e.Scales = 10;
                Check(!e.Apply(VaultAction.Expand, default, default, (int)Expansion.Storage, 1) && e.StorageSlots == 40, "storage bought without the scales");
                e.Scales = 1000;
                Check(e.Apply(VaultAction.Expand, default, default, (int)Expansion.Storage, 1) && e.StorageSlots == 55, "storage +15 slots");
                var e2 = VaultData.Read(e.Write());
                Check(e2.SlotsOf(0) == 10 && e2.StorageSlots == 55, "expansions lost in a save");
                foreach (Expansion x in Enum.GetValues(typeof(Expansion)))
                {
                    var prices = GearCatalog.PricesOf(x);
                    for (int i = 1; i < prices.Length; i++) Check(prices[i].scales > prices[i - 1].scales && prices[i].best >= prices[i - 1].best, $"{x} step {i + 1} isn't dearer");
                }
                // The vault: scales only, no wave gate, up to 130 slots.
                var sp = GearCatalog.StoragePrices;
                Check(sp.All(p => p.best == 0) && GearCatalog.StorageBase + GearCatalog.StorageStep * sp.Length == 190,
                      "vault steps: " + string.Join(", ", sp.Select(p => $"{p.scales}@{p.best}")));
                // Equipping into a bought slot works; one past it doesn't.
                var f = new VaultData { Scales = 1000, Best = 100 };
                f.Apply(VaultAction.Expand, default, default, (int)Expansion.TeamSlot, 1);
                var team = GearCatalog.All.Where(GearCatalog.IsFortress).Take(6).ToList();
                foreach (var g in team) f.Add(g, Rarity.Common);
                int on = team.Count(g => f.Apply(VaultAction.EquipFortress, g, Rarity.Common, 0, 1));
                Check(on == 5, $"equipped {on} team gear with 5 slots");
            }
            // The same kind worn twice adds up (Owen: "同一件裝備可以疊"): a cut multiplies down and never reaches 100%, the lucky
            // charm adds its extra chance, the rest add.
            {
                var d = new VaultData();
                d.Add(Gear.SharkArmor, Rarity.Legendary, 3); d.Add(Gear.Gunpowder, Rarity.Epic, 2); d.Add(Gear.LuckyCharm, Rarity.Legendary, 2);
                Check(d.Apply(VaultAction.EquipPersonal, Gear.SharkArmor, Rarity.Legendary, 0, 9) && d.Apply(VaultAction.EquipPersonal, Gear.SharkArmor, Rarity.Legendary, 0, 9)
                      && !d.Apply(VaultAction.EquipPersonal, Gear.SharkArmor, Rarity.Legendary, 0, 9), "two shark armours in two slots (and no third)");
                float armor = d.PersonalValue(9, Gear.SharkArmor), one = GearCatalog.Value(Gear.SharkArmor, Rarity.Legendary);
                Check(Mathf.Abs(armor - (1f - (1f - one) * (1f - one))) < 1e-5f && armor < 1f, $"two legendary armours cut {armor:P0}");
                Check(d.Apply(VaultAction.EquipFortress, Gear.Gunpowder, Rarity.Epic, 0, 9) && !d.Apply(VaultAction.EquipFortress, Gear.Gunpowder, Rarity.Epic, 0, 9)
                      && Mathf.Abs(d.FortressValue(Gear.Gunpowder) - GearCatalog.Value(Gear.Gunpowder, Rarity.Epic)) < 1e-5f, "a second gunpowder is refused (one per tower gear kind)");
                Check(Mathf.Abs(GearCatalog.Stacked(Gear.LuckyCharm, new[] { 3f, 3f }) - 5f) < 1e-5f && GearCatalog.Stacked(Gear.LuckyCharm, new float[0]) == 0f
                      && GearCatalog.Stacked(Gear.ToolBelt, Enumerable.Repeat(0.25f, 4)) < 1f, "lucky charms and tool belts stack");
            }
            // One-click sale (Owen: "一鍵賣"): a tab and a colour; weapons for money as at the counter, gear melted, the game's
            // items only with every colour; what's worn stays; what the button says is what's sold.
            {
                var s = new VaultData();
                s.Add(Gear.Laser, Rarity.Epic, 3); s.Add(Gear.Rocket, Rarity.Common, 2); s.Add(Gear.Scope, Rarity.Epic, 2); s.Add(Gear.Decoy, Rarity.Rare, 1);
                s.Fortress.Add((Gear.Scope, Rarity.Epic));
                s.AddItem(new StoredItem { Saved = new SavedItem { Exists = true, ItemID = 37 } }); s.AddItem(new StoredItem { Saved = new SavedItem { Exists = true, ItemID = 12 } });
                var preview = s.SaleOf(2, Rarity.Epic);
                var sold = s.SellAll(2, Rarity.Epic, _ => 100);
                Check(sold == preview && sold.money == 3 * WeaponItems.Worth(Rarity.Epic) && sold.scales == 0 && s.Count(Gear.Laser, Rarity.Epic) == 0 && s.Count(Gear.Rocket, Rarity.Common) == 2
                      && s.Count(Gear.Scope, Rarity.Epic) == 2 && s.ItemCount.Count == 2, $"epic weapons sold: {sold}");
                int scales = s.Scales;
                sold = s.SellAll(1, null, _ => 100);
                Check(sold.scales == 2 * GearCatalog.SalvageScales[(int)Rarity.Epic] + GearCatalog.SalvageScales[(int)Rarity.Rare] && s.Scales == scales + sold.scales && sold.money == 0
                      && s.Count(Gear.Rocket, Rarity.Common) == 2 && s.Fortress.Count == 1, $"gear melted: {sold}");
                Check(s.SaleOf(0, null).count == 4, "everything left: 2 rockets and 2 of the game's items");
                sold = s.SellAll(0, null, _ => 100);
                Check(sold.money == 2 * WeaponItems.Worth(Rarity.Common) + 200 && s.Owned.Count == 0 && s.Items.Count == 0 && s.ItemCount.Count == 0 && s.Fortress.Count == 1, $"sold out: {sold}");
                Check(s.SellAll(3, null, _ => 100).count == 0, "nothing left to sell");
            }
            // Slots: 50 of one thing to a slot; a part-filled stack takes one more even in a full vault.
            {
                var st = new VaultData();
                st.Add(Gear.Laser, Rarity.Common, 49);
                Check(st.SlotsUsed == 1, "49 lasers in one slot");
                st.Add(Gear.Laser, Rarity.Common, 1);
                Check(st.SlotsUsed == 1, "50 lasers still one slot");
                st.Add(Gear.Laser, Rarity.Common, 1);
                Check(st.SlotsUsed == 1, "51 lasers stay in their one (gear, rarity) slot; stars are sub-stacks");
                int gi = 0;
                while (st.SlotsUsed < st.StorageSlots) st.Add(GearCatalog.All[gi++ % GearCatalog.All.Length], Rarity.Common, 1);
                Check(st.VaultFull && st.Fits(Gear.Laser, Rarity.Common) && !st.Fits(Gear.Scope, Rarity.Rare),
                      $"full vault ({st.SlotsUsed}/{st.StorageSlots}): only an existing group or a free slot takes more");
                var fish = new StoredItem { Dead = true, Saved = new SavedItem { Exists = true, ItemID = 37, Weight = 1.734f, Cookness = 0.25f, BettingMultiplier = 1f, KillScoreMultiplier = 1.5f, SkinIndex = 2, IsDripCreature = true } };
                var gun = new StoredItem { Saved = new SavedItem { Exists = true, ItemID = 12, Weight = 1f, Sight = 3, AmmoType = 1, ExtendedMag = true, LaserSight = true, Sharpness = 0, Quiver = 0, BarrelAttachment = 2 } };
                st.AddItem(fish); st.AddItem(gun); st.AddItem(fish);
                Check(st.CountItem(37) == 2 && st.CountItem(12) == 1 && st.SlotsUsed == st.StorageSlots + 2, "the game's items count in slots too");
                var back2 = VaultData.Read(st.Write());
                var f2 = back2.Items.First(i => i.Id == 37).Saved;
                var g2 = back2.Items.First(i => i.Id == 12).Saved;
                Check(back2.Items.Count == 3 && back2.Items[0].Dead && Math.Abs(f2.Weight - 1.734f) < 1e-6f && Math.Abs(f2.Cookness - 0.25f) < 1e-6f && f2.SkinIndex == 2 && f2.IsDripCreature
                      && g2.Sight == 3 && g2.AmmoType == 1 && g2.ExtendedMag && g2.LaserSight && g2.BarrelAttachment == 2, "a stored fish and gun come back from the file as they were");
                var client = VaultData.Read(st.Write(forClients: true));
                Check(client.Items.Count == 0 && client.CountItem(37) == 2 && client.CountItem(12) == 1, "clients get counts, not the items");
                Check(st.TakeItem(37)?.Id == 37 && st.CountItem(37) == 1 && st.TakeItem(99) == null, "taking one out");
            }

            // Stars: three of the same gear/rarity/star forge the next star; three 3★ forge the next rarity at 0★.
            {
                var st = new VaultData();
                st.Add(Gear.Laser, Rarity.Common, 3);
                Check(st.Apply(VaultAction.Merge, Gear.Laser, Rarity.Common, 0, 1, 0) && st.Count(Gear.Laser, Rarity.Common, 0) == 0
                      && st.Count(Gear.Laser, Rarity.Common, 1) == 1, "3 x 0-star -> 1-star");
                st.AddStar(Gear.Laser, Rarity.Common, 1, 2);
                Check(st.Count(Gear.Laser, Rarity.Common, 1) == 3 && st.Mergeable() >= 1, "three 1-stars can merge");
                st.MergeAll();
                Check(st.Count(Gear.Laser, Rarity.Common, 1) == 0 && st.Count(Gear.Laser, Rarity.Common, 2) == 1, "three 1-star -> 2-star");
                st.AddStar(Gear.Laser, Rarity.Epic, 3, 3);
                st.MergeAll();
                Check(st.Count(Gear.Laser, Rarity.Legendary, 0) == 1 && st.Count(Gear.Laser, Rarity.Epic, 3) == 0, "three epic 3-star -> legendary 0-star");
                Check(Mathf.Abs(st.ValueOf(Gear.Laser, Rarity.Common, 3) - GearCatalog.Value(Gear.Laser, Rarity.Common) * 1.24f) < 1e-5f, "a 3-star is +24%");
                Check(st.SlotsUsed == 2, "four star substacks of one gear/rarity still take one slot");
                var file = VaultData.Read(st.Write());
                Check(file.Count(Gear.Laser, Rarity.Legendary, 0) == 1 && file.Count(Gear.Laser, Rarity.Common, 2) == 1, "stars survive a vault save");
                var oldStar = VaultData.Read("own Laser Rare 2\nfort Rebar Legendary\npers 7 SharkArmor Rare");
                Check(oldStar.Count(Gear.Laser, Rarity.Rare, 0) == 2 && oldStar.Fortress[0].Stars == 0 && oldStar.Slots(7)[0].Stars == 0, "old saves load as 0-star");
            }

            var v = new VaultData();
            v.Add(Gear.Scope, Rarity.Epic, 2);
            v.Add(Gear.Decoy, Rarity.Common, 1);
            v.Fortress.Add((Gear.Rebar, Rarity.Legendary));
            v.Slots(76561198009466774UL).Add((Gear.SharkArmor, Rarity.Rare));
            v.Slots(76561198009466774UL).Add((Gear.LuckyCharm, Rarity.Epic)); // (active gear isn't worn: see the active checks)
            v.Names[76561198009466774UL] = "王 小明  the 2nd";
            v.Unlocked.Add(PieceType.Sniper);
            v.Scales = 37; v.Enhance[Gear.Rebar] = 2; v.Best = 14;
            v.Mods[(76561198009466774UL, Gear.Rocket, WeaponMods.Trait)] = 3; v.Mods[(76561198009466774UL, Gear.Smg, WeaponMods.Magazine)] = 5;
            v.Wins.Add(("2026-10-18", "王 小明  the 2nd、Owen"));
            v.Wins.Add(("2026-10-19", "Solo"));
            var back = VaultData.Read(v.Write());
            Check(back.Wins.Count == 2 && back.Wins[0].team == "王 小明  the 2nd、Owen" && back.Wins[1].date == "2026-10-19", "OVER 99 wins survive the file");
            Check(back.Write() == v.Write(), "vault file round trip:\n" + v.Write() + "\n--- read back as ---\n" + back.Write());
            Check(back.Names[76561198009466774UL] == "王 小明  the 2nd", "names with spaces survive the file");
            Check(VaultData.Read("own Scope Epic 2\ngarbage line\nown NotAGear Rare 1\nfort Rebar\nscales 5 0").Count(Gear.Scope, Rarity.Epic) == 2, "a bad line never loses the rest");
            // ...and what this version can't read (a newer one's gear) is written back as it was, not dropped at the next save.
            var newer = VaultData.Read("own Laser Epic 2\nown Railgun Legendary 1\nscales 5 0\nshiny 1 2 3");
            Check(newer.Count(Gear.Laser, Rarity.Epic) == 2 && newer.Write().Contains("own Railgun Legendary 1") && newer.Write().Contains("shiny 1 2 3")
                  && !newer.Write(forClients: true).Contains("Railgun") && VaultData.Read(newer.Write()).Write() == newer.Write(), "unknown vault lines were dropped:\n" + newer.Write());
            // The vault file is written like the game's saves (a temporary file swapped in, the last one kept as .backup), and a
            // file cut short or gone is read from its backup.
            {
                string dir = Path.Combine(Path.GetTempPath(), "htf-vault-" + Guid.NewGuid().ToString("N")), path = Path.Combine(dir, "v.txt");
                Check(Loot.WriteSafely(path, "scales 1 0") && Loot.WriteSafely(path, "scales 2 0") && File.ReadAllText(path) == "scales 2 0"
                      && File.ReadAllText(path + ".backup") == "scales 1 0" && !File.Exists(path + ".tmp"), "vault written in place, or without its backup");
                File.WriteAllText(path, "");
                Check(Loot.ReadSafely(path) == "scales 1 0", "an emptied vault file isn't read from its backup");
                File.Delete(path);
                Check(Loot.ReadSafely(path) == "scales 1 0" && Loot.ReadSafely(Path.Combine(dir, "none.txt")) == null, "a missing vault file isn't read from its backup");
                Directory.Delete(dir, true);
            }
            // An old save: a weapon's team enhancement becomes the power mod of everyone who has played on it; gear keeps its own.
            var old = VaultData.Read("enh Laser 3\nenh Rebar 2\nenh Rocket 9\nstarter 5 1\nname 6 Bob\npers 7 SharkArmor Rare\nmod 5 Laser 0 4");
            Check(old.ModOf(5, Gear.Laser, WeaponMods.Power) == 4 && old.ModOf(6, Gear.Laser, WeaponMods.Power) == 3 && old.ModOf(7, Gear.Laser, WeaponMods.Power) == 3
                  && old.ModOf(6, Gear.Rocket, WeaponMods.Power) == GearCatalog.MaxEnhance && old.EnhanceOf(Gear.Laser) == 0 && old.EnhanceOf(Gear.Rebar) == 2,
                  "old weapon enhancement didn't become everyone's power:\n" + old.Write());

            // Random clicking by three players: nothing appears from nowhere, slots stay legal, scales never go negative.
            var rng = new System.Random(3);
            ulong[] keys = { 1, 2, 3 };
            v = new VaultData();
            int kinds = GearCatalog.All.Length;
            for (int i = 0; i < 60; i++) v.Add((Gear)rng.Next(kinds), (Rarity)rng.Next(4));
            int applied = 0;
            for (int step = 0; step < 50000; step++)
            {
                if (rng.Next(10) == 0) v.Add((Gear)rng.Next(kinds), (Rarity)rng.Next(4)); // a drop
                var a = rng.Next(9) == 8 ? VaultAction.Mod : (VaultAction)rng.Next(8);
                Gear g = (Gear)rng.Next(kinds + 1); // the last one is not a gear (a bad packet)
                Rarity r = (Rarity)rng.Next(5);
                int slot = rng.Next(-1, 4);
                ulong key = keys[rng.Next(3)];
                // Taking off names what comes off: mostly something that's on, sometimes something that isn't.
                var on = a == VaultAction.UnequipFortress ? v.Fortress : a == VaultAction.UnequipPersonal ? v.Slots(key) : null;
                if (on != null && on.Count > 0 && rng.Next(4) > 0) { var pick = on[rng.Next(on.Count)]; g = pick.Item1; r = pick.Item2; }
                int items = Items(v), scales = v.Scales, count = v.Count(g, r), enh = v.EnhanceOf(g), mod = v.ModOf(key, g, slot);
                bool ok = v.Apply(a, g, r, slot, key);
                if (ok) applied++;
                int dItems = Items(v) - items, dScales = v.Scales - scales;
                string act = $"{a} {g} {r} slot {slot} by {key}";
                if (!ok) { Check(dItems == 0 && dScales == 0, $"refused {act} still changed the vault"); continue; }
                switch (a)
                {
                    case VaultAction.Merge: Check(dItems == -2 && dScales == 0 && count >= 3, $"{act}: merge took {-dItems + 1} for 1"); break;
                    case VaultAction.Salvage: Check(dItems == -1 && dScales == GearCatalog.SalvageScales[(int)r], $"{act}: salvage paid {dScales}"); break;
                    case VaultAction.Ascend: Check(dItems == 0 && dScales == -GearCatalog.AscendCost(r) && r < Rarity.Legendary, $"{act}: rank up cost {-dScales}"); break;
                    case VaultAction.Enhance: Check(dItems == 0 && dScales == -GearCatalog.EnhanceCosts[enh] && v.EnhanceOf(g) == enh + 1 && !GearCatalog.IsWeapon(g), $"{act}: enhance"); break;
                    case VaultAction.Mod: Check(dItems == 0 && dScales == -WeaponMods.Cost(mod) && v.ModOf(key, g, slot) == mod + 1 && WeaponMods.Has(g, slot), $"{act}: mod"); break;
                    default: Check(dItems == 0 && dScales == 0, $"{act}: equipping changed the item count by {dItems}"); break;
                }
                Check(v.Scales >= 0, $"{act}: scales went negative");
                Check(v.Owned.Values.All(n => n > 0), $"{act}: an empty stack stayed in the vault");
                Check(v.Fortress.Count <= v.SlotsOf(0) && v.Fortress.All(f => GearCatalog.IsFortress(f.Item1)), $"{act}: fortress loadout broke the rules");
                foreach (var kv in v.Personal)
                    Check(Enumerable.Range(1, 3).All(k => kv.Value.Count(x => GearCatalog.SlotKind(x.Item1) == k) <= GearCatalog.SlotsOf(k))
                          && kv.Value.All(x => !GearCatalog.IsFortress(x.Item1) && !GearCatalog.IsActive(x.Item1)), $"{act}: personal slots broke the rules");
                Check(v.Enhance.Values.All(n => n >= 1 && n <= GearCatalog.MaxEnhance), $"{act}: enhancement out of range");
                Check(v.Mods.All(m => m.Value >= 1 && WeaponMods.Has(m.Key.Item2, m.Key.Item3)), $"{act}: a mod out of range");
                Check(v.Owned.Keys.All(k => (int)k.Item1 < kinds && (int)k.Item2 < 4), $"{act}: a gear that doesn't exist got into the vault");
            }
            Check(VaultData.Read(v.Write()).Write() == v.Write(), "fuzzed vault survives the file");
            Console.WriteLine($"  50000 random actions, {applied} allowed; {Items(v)} items, {v.Scales} scales at the end");

            // No loop farms scales: whatever you make is worth no more (in scales) than what went in.
            for (int r = 0; r < 3; r++)
            {
                Check(GearCatalog.SalvageScales[r + 1] <= 3 * GearCatalog.SalvageScales[r], $"merging {(Rarity)r} then dismantling beats dismantling the three");
                int c = GearCatalog.AscendCost((Rarity)r);
                if (c > 0) Check(GearCatalog.SalvageScales[r + 1] < c + GearCatalog.SalvageScales[r], $"ranking up {(Rarity)r} then dismantling makes scales");
            }

            // Active gear is used up, one at a time (Owen: "k的技能那些要是耗材"), so it doesn't merge by itself any more (three uses
            // would become one) and has no cooldown of its own; it isn't worn: one worn in an old vault file comes back into the
            // vault, and wearing one is refused.
            var am = VaultData.Read("own Overclock Common 9");
            Check(am.Count(Gear.Overclock, Rarity.Common) == 9, "nine overclocks merged by themselves");
            var worn = VaultData.Read("pers 7 Overclock Rare\npers 7 SharkArmor Common");
            Check(worn.Count(Gear.Overclock, Rarity.Rare) == 1 && worn.Slots(7).Count == 1, "an active worn in an old vault file didn't come back into the vault");
            worn.Add(Gear.Decoy, Rarity.Common);
            Check(!worn.Apply(VaultAction.EquipPersonal, Gear.Decoy, Rarity.Common, 0, 7), "active gear can still be worn");
            // What K can use: each kind the team has once, at its best colour; kinds it hasn't got are skipped (this hung the game
            // once: a byte rarity counting down past common never ends).
            worn.Add(Gear.Decoy, Rarity.Epic);
            var usable = Actives.Owned(worn);
            Check(usable.Count == 2 && usable.Contains((Gear.Overclock, Rarity.Rare, 0)) && usable.Contains((Gear.Decoy, Rarity.Epic, 0)), $"K can use {string.Join(", ", usable)}");
            Check(Actives.Owned(new VaultData()).Count == 0, "K can use something from an empty vault");
        }

        // ---------- buildings ----------

        private static float Dps(Catalog.TurretStats s) => s.Interval > 0f ? s.Damage * s.Shots / s.Interval : 0f;

        private static void Buildings()
        {
            Section("buildings");
            // A turret's ring of lamps: one more lit a level past Lv3 (the +N on its card), the ring full at Lv20 and no further.
            for (int l = 1; l <= 30; l++) Check(BuildSystem.LampsLit(l) == Math.Min(BuildSystem.Lamps, Math.Max(0, l - 3)), $"Lv{l} lights {BuildSystem.LampsLit(l)} lamps");
            Check(BuildSystem.LampsLit(20) == BuildSystem.Lamps, "the ring of lamps isn't full at Lv20");
            var all = (PieceType[])Enum.GetValues(typeof(PieceType));
            Check(Catalog.TypeCount == all.Length, $"Catalog.TypeCount {Catalog.TypeCount} but {all.Length} piece types (the build bar uses it)");
            foreach (var t in all)
            {
                Check(Catalog.BuildCost(t) > 0, $"{t} costs nothing");
                // No level cap: health keeps rising, every step costs more than the one before, nothing overflows far out.
                for (int l = 1; l <= 20; l++) Check(Catalog.MaxHp(t, l) > 0 && (l == 1 || Catalog.MaxHp(t, l) > Catalog.MaxHp(t, l - 1) || l <= Catalog.BranchLevel && Catalog.MaxHp(t, l) >= Catalog.MaxHp(t, l - 1)), $"{t} Lv{l} health");
                for (int l = 1; l <= 20; l++) Check(Catalog.UpgradeCost(t, l) > 0 && (l == 1 || Catalog.UpgradeCost(t, l) > Catalog.UpgradeCost(t, l - 1)), $"{t} Lv{l}->{l + 1} upgrade isn't dearer than the last");
                Check(Catalog.UpgradeCost(t, 200) > 0 && Catalog.MaxHp(t, 200) > 0, $"{t}: absurd levels overflow");
                var size = Catalog.Size(t);
                Check(size.x > 0 && size.y > 0 && size.z > 0, $"{t} has no size");
                Check(Catalog.NeedsBlueprint(t) == (t >= PieceType.Flame && t != PieceType.Mender), $"{t} blueprint rule");
                if (!Catalog.IsTurret(t)) continue;
                for (int l = 1; l <= 16; l++)
                    foreach (int b in l >= Catalog.BranchLevel ? new[] { 1, 2 } : new[] { 0 })
                    {
                        var s = Catalog.Turret(t, l, b);
                        if (l > Catalog.BranchLevel && t != PieceType.Bubble)
                            Check(Dps(s) * s.Targets > Dps(Catalog.Turret(t, l - 1, b)) * Catalog.Turret(t, l - 1, b).Targets, $"{t} Lv{l}/{b} no stronger than Lv{l - 1}");
                        Check(s.Interval > 0f && s.Range > s.MinRange && s.Shots >= 1, $"{t} Lv{l}/{b} stats");
                        if (t == PieceType.Bubble) Check(s.Shield > 0f && s.Shield < 1f, "bubble shield in (0,1)");
                        else Check(Dps(s) > 0f, $"{t} Lv{l} does no damage");
                    }
                if (t != PieceType.Bubble)
                {
                    float d1 = Dps(Catalog.Turret(t, 1, 0)), d2 = Dps(Catalog.Turret(t, 2, 0)), d3 = Math.Max(Dps(Catalog.Turret(t, 3, 1)), Dps(Catalog.Turret(t, 3, 2)));
                    Check(d1 <= d2 && d2 <= d3, $"{t} damage per second doesn't rise with level ({d1:0} / {d2:0} / {d3:0})");
                }
            }
            for (int first = 1; first <= 2; first++)
                for (int second = 1; second <= 2; second++)
                {
                    int code = Catalog.BranchCode(first, second);
                    Check(Catalog.FirstOf(code) == first && Catalog.SecondOf(code) == second,
                          $"branch code {first}/{second} -> {code} maps back to {Catalog.FirstOf(code)}/{Catalog.SecondOf(code)}");
                    Check(Catalog.BranchKey(PieceType.Flame, code) == "Flame" + (first == 1 ? "A" : "B") + second,
                          $"branch key {code} is {Catalog.BranchKey(PieceType.Flame, code)}");
                }
            Check(Catalog.BranchKey(PieceType.Flame, 5) != Catalog.BranchKey(PieceType.Flame, 6), "the two B-side second branches are different");
            var v = new VaultData();
            Check(all.Count(v.CanBuild) == 10, "10 pieces buildable without blueprints (the repair tower among them)");
            var mend = Catalog.Turret(PieceType.Mender, 1, 0);
            Check(!Catalog.Shoots(PieceType.Mender) && !Catalog.Shoots(PieceType.Bubble) && Catalog.Shoots(PieceType.Gun) && mend.Damage > 0 && mend.Range > 0f
                  && Catalog.Turret(PieceType.Mender, 3, 1).Range > Catalog.Turret(PieceType.Mender, 3, 2).Range && Catalog.Turret(PieceType.Mender, 3, 2).Damage > Catalog.Turret(PieceType.Mender, 3, 1).Damage,
                  "repair tower: heals, doesn't shoot; A reaches further, B heals more");
            v.Unlocked.Add(PieceType.Bubble);
            Check(v.CanBuild(PieceType.Bubble) && !v.CanBuild(PieceType.Sniper), "a blueprint unlocks just its turret");
        }

        // ---------- enemies ----------

        private static void EnemyTable()
        {
            Section("enemies");
            foreach (EnemyKind k in Enum.GetValues(typeof(EnemyKind)))
            {
                var s = EnemyCatalog.Of(k);
                Check(s.BaseHp > 0 && s.Scale > 0 && s.Reward >= 1 && s.Speed >= 0, $"{k} stats");
                var e = Enemies.StatsOf(new EnemyTag { Kind = k, Elite = true });
                Check(e.BaseHp == s.BaseHp * EnemyCatalog.EliteHp && e.Reward == s.Reward * EnemyCatalog.EliteReward, $"elite {k}");
            }
            // README's enemy table (first wave each kind shows up).
            var readme = new Dictionary<EnemyKind, int> { [EnemyKind.Runner] = 1, [EnemyKind.Charger] = 3, [EnemyKind.Thief] = 4, [EnemyKind.Spitter] = 5, [EnemyKind.Splitter] = 5,
                [EnemyKind.Bomber] = 6, [EnemyKind.Healer] = 6, [EnemyKind.Shield] = 7, [EnemyKind.Tank] = 8, [EnemyKind.Digger] = 9 };
            foreach (var kv in readme) Check(EnemyCatalog.Of(kv.Key).FirstWave == kv.Value, $"README says {kv.Key} from wave {kv.Value}, code says {EnemyCatalog.Of(kv.Key).FirstWave}");
            // Our own fish over the game's walking one (Foes.cs): every land kind has its body, and it lands on the game's body:
            // as long, centred on it, nose to its head, its own proportions whatever the kind's stretch (numbers: the game's).
            foreach (EnemyKind k in Enum.GetValues(typeof(EnemyKind)))
                if (!EnemyCatalog.IsPuppet(k))
                    Check(File.Exists(Path.Combine(Src, "Models", FoeBodies.ModelOf(k) + ".obj")) && File.Exists(Path.Combine(Src, "Models", FoeBodies.ModelOf(k) + ".mtl")), $"{k}: no body model");
            Check(FoeBodies.Snap(new Vector3(0.1f, -0.9f, 0.2f)) == Vector3.down && FoeBodies.Snap(new Vector3(-0.97f, 0.05f, 0.2f)) == Vector3.left, "snapping to the nearest axis");
            {
                var game = new Bounds(new Vector3(0.03f, 0.001f, -0.066f), new Vector3(0.911f, 0.098f, 0.405f)); // the game's body: head -x, up +z
                var ours = new Bounds(new Vector3(0f, 0.01f, -0.005f), new Vector3(0.28f, 0.4f, 0.99f));         // ours: nose +z, up +y
                foreach (var shape in new[] { Vector3.one, Enemies.ShapeOf(EnemyKind.Charger), Enemies.ShapeOf(EnemyKind.Tank) })
                {
                    Vector3 fwd = Vector3.left, up = Vector3.forward, right = Vector3.Cross(up, fwd);
                    var (scale, pos) = FoeBodies.Fit(fwd, up, game, ours, shape);
                    Vector3 Map(Vector3 v) => pos + right * (scale.x * v.x) + up * (scale.y * v.y) + fwd * (scale.z * v.z);
                    Vector3 world = Vector3.Scale(scale, shape); // the walker's stretch on top: ours must come out even
                    Check(Mathf.Abs(world.x - world.z) < 1e-4f && Mathf.Abs(world.y - world.z) < 1e-4f, $"stretch {shape}: our body comes out {world}");
                    Vector3 nose = Map(new Vector3(0f, 0f, ours.max.z)), mid = Map(ours.center);
                    Check(Mathf.Abs(nose.x - game.min.x) < 0.01f && Mathf.Abs(mid.x - game.center.x) < 0.01f && mid.z > game.center.z && mid.z - game.center.z < 0.05f,
                          $"stretch {shape}: nose at {nose}, middle at {mid} (the game's head end {game.min.x:F3}, middle {game.center})");
                }
            }
            // OVER 99: ten boss waves, ten different bosses (all ours), the Eye last; each has a body, a brain and a colour.
            var order = Enumerable.Range(1, 10).Select(i => EnemyCatalog.BossFor(i * 10)).ToList();
            Check(order.Distinct().Count() == 10, "the ten boss waves don't have ten different bosses: " + string.Join(", ", order));
            Check(order.SequenceEqual(EnemyCatalog.BossOrder), "BossFor doesn't follow BossOrder");
            Check(EnemyCatalog.BossFor(EnemyCatalog.FinalWave) == EnemyKind.Eye, "wave 100 isn't the Eye");
            Check(EnemyCatalog.BossFor(10) == EnemyKind.Warden && EnemyCatalog.BossFor(20) == EnemyKind.Urchin, "the first boss waves");
            foreach (EnemyKind k in Enum.GetValues(typeof(EnemyKind)))
            {
                if (!EnemyCatalog.IsPuppet(k)) { Check(BossLooks.BodyOf(k) == null, $"{k}: a walker with a puppet body"); continue; }
                Check(!string.IsNullOrEmpty(BossLooks.BodyOf(k)), $"{k}: no body");
                Check(EnemyCatalog.Of(k).Scale > 1f && EnemyCatalog.Of(k).Gear == null, $"{k}: a body blown up, no walker gear");
                if (!EnemyCatalog.IsBoss(k)) continue;
                Check(EnemyCatalog.BossOrder.Contains(k), $"{k}: a boss that never comes");
                Check(BossBrains.Make(k)?.GetType().Name == k.ToString(), $"{k}: brain {BossBrains.Make(k)?.GetType().Name}");
                Check(BossLooks.Colour(k) != BossLooks.Colour(EnemyKind.Runner), $"{k}: no colour of its own");
            }
            Check(EnemyCatalog.BossOrder.Select(BossLooks.Colour).Distinct().Count() == 10, "two bosses share a colour");
            // Every boss effect the host sends has a look on the clients: an unknown key only logs "BossFx: no effect" there.
            string looks = File.ReadAllText(Path.Combine(Src, "BossMoves.cs")) + File.ReadAllText(Path.Combine(Src, "BossFx.cs"));
            var sent = Sources.Where(f => Path.GetFileName(f).StartsWith("Boss"))
                .SelectMany(f => new Regex(@"Send\(""([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)?)""").Matches(File.ReadAllText(f)).Cast<Match>())
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            var unseen = sent.Where(k => !looks.Contains($"case \"{k}\"")).ToList();
            Check(sent.Count >= 90 && unseen.Count == 0, $"{sent.Count} boss effects sent, with no look: {string.Join(", ", unseen)}");
            // Their trophies: items 180-189 (after the game's 0-102 and the rocket and guns' 150-177, before the first weapons' 200-255), one each, worth more the later the boss.
            var ids = EnemyCatalog.BossOrder.Select(BossTrophies.IdOf).ToList();
            Check(ids.Distinct().Count() == 10 && ids.Min() == BossTrophies.FirstId && ids.Max() == BossTrophies.FirstId + 9, "trophy ids: " + string.Join(",", ids));
            var weaponIds = WeaponItems.Kinds.SelectMany(g => GearCatalog.Rarities.Select(r => (int)WeaponItems.IdOf(g, r))).ToList();
            Check(ids.All(i => i > 102 && !weaponIds.Contains(i)) && weaponIds.Distinct().Count() == weaponIds.Count && weaponIds.Min() > 102 && weaponIds.Max() <= 255, "trophy ids overlap the game's or the weapons'");
            Check(EnemyCatalog.BossOrder.Select(BossTrophies.Worth).Zip(EnemyCatalog.BossOrder.Skip(1).Select(BossTrophies.Worth), (x, y) => y > x).All(b => b), "trophies don't get worth more boss by boss");
            foreach (var k in EnemyCatalog.BossOrder) Check(File.Exists(Path.Combine(Src, "Models", BossTrophies.ModelOf(k) + ".obj")), $"{k}: no trophy model");
            // The Eye's own body (BossEye.cs): all its parts there, the eyeball inside its lids, the lids inside the socket and the
            // corona clear of it (a radius apart: however they turn, nothing shows through), and its eyeball turns no further than MaxLook.
            foreach (var p in BossEye.Parts) Check(File.Exists(Path.Combine(Src, "Models", p + ".obj")) && File.Exists(Path.Combine(Src, "Models", p + ".mtl")), $"no {p} model");
            (float lo, float hi) Radii(string n)
            {
                var r = File.ReadLines(Path.Combine(Src, "Models", n + ".obj")).Where(l => l.StartsWith("v ")).Select(l => l.Split(' ').Skip(1).Select(f => float.Parse(f, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
                    .Select(q => (float)Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2])).ToList();
                return (r.Min(), r.Max());
            }
            var ball = Radii("BossEyeBall");
            var lids = new[] { Radii("BossEyeLidTop"), Radii("BossEyeLidBottom") };
            var socket = Radii("BossEyeShell");
            var corona = Radii("BossEyeHalo");
            Check(lids.All(l => ball.hi < l.lo && l.hi < socket.lo) && socket.hi < corona.lo, $"the Eye: ball out to {ball.hi:F2} m, lids {lids[0].lo:F2}-{lids[0].hi:F2}, socket {socket.lo:F2}-{socket.hi:F2}, corona from {corona.lo:F2}");
            foreach (var d in new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, -1f, 0.2f), new Vector3(0f, 0f, -1f), new Vector3(0.3f, 0.2f, -1f) })
            {
                var l = BossEye.Look(d, BossEye.MaxLook);
                Check(Math.Abs(Vector3.Angle(Vector3.forward, l) - BossEye.MaxLook) < 0.1f && Math.Abs(l.magnitude - 1f) < 1e-4f, $"the Eye looking toward {d}: {l}, not {BossEye.MaxLook} degrees round");
            }
            var ahead = new Vector3(0.1f, 0.2f, 1f);
            Check(Vector3.Angle(BossEye.Look(ahead, BossEye.MaxLook), ahead) < 0.01f && BossEye.Look(Vector3.zero, BossEye.MaxLook) == Vector3.forward, "the Eye doesn't look straight at what's in front of it");
            // The Steed's own body (BossSteed.cs): all its parts there, each upper leg reaching down to the joint its lower half
            // turns on, and its hooves standing where the game's horse had them (BossSteed.Hooves under the root: the brain's
            // footing, and the hind hooves it rears about).
            foreach (var p in BossSteed.Parts) Check(File.Exists(Path.Combine(Src, "Models", p + ".obj")) && File.Exists(Path.Combine(Src, "Models", p + ".mtl")), $"no {p} model");
            (float lo, float hi) Heights(string n)
            {
                var y = File.ReadLines(Path.Combine(Src, "Models", n + ".obj")).Where(l => l.StartsWith("v ")).Select(l => float.Parse(l.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture)).ToList();
                return (y.Min(), y.Max());
            }
            Check(Heights("BossSteedForeArm").lo <= BossSteed.Knee.y + 0.05f && Heights("BossSteedForeShin").hi >= -0.05f, "the Steed's foreleg comes apart at the knee");
            Check(Heights("BossSteedThigh").lo <= BossSteed.Hock.y + 0.05f && Heights("BossSteedHindShin").hi >= -0.05f, "the Steed's hind leg comes apart at the hock");
            float fore = BossSteed.Shoulder.y + BossSteed.Knee.y + Heights("BossSteedForeShin").lo, hind = BossSteed.Hip.y + BossSteed.Hock.y + Heights("BossSteedHindShin").lo;
            Check(Math.Abs(fore + BossSteed.Hooves) < 0.05f && Math.Abs(hind + BossSteed.Hooves) < 0.05f, $"the Steed's hooves {fore:F2} / {hind:F2} m under its root, not {BossSteed.Hooves}");
        }

        // ---------- drops and chests ----------

        private static void LootRolls()
        {
            Section("loot");
            const int N = 200000;
            foreach (var (wave, expect) in new[] { (1, GearCatalog.RollFirst), (20, GearCatalog.RollLate), (40, GearCatalog.RollLate) })
            {
                var c = new int[4];
                for (int i = 0; i < N; i++) c[(int)GearCatalog.Roll(wave)]++;
                float sum = expect.Sum();
                for (int i = 0; i < 4; i++) Check(Math.Abs(100.0 * c[i] / N - 100.0 * expect[i] / sum) < 0.6, $"wave {wave} {(Rarity)i}: {100.0 * c[i] / N:0.0}% (table {100.0 * expect[i] / sum:0.0}%)");
            }
            Check(Enumerable.Range(0, 2000).All(_ => GearCatalog.Roll(1, Rarity.Epic) >= Rarity.Epic), "boss drops are epic or better");
            var kinds = new int[GearCatalog.All.Length];
            for (int i = 0; i < N; i++) kinds[(int)GearCatalog.RandomGear()]++;
            // Each kind of gear as likely as before the rocket and the game's guns came; weapons as a whole too, any of them alike;
            // a special weapon (the absorber) never: only the smithy sells it.
            int before = (int)Gear.LobsterClaw + 1, weaponKinds = GearCatalog.Droppable.Length;
            int retiredCount = Enumerable.Range(0, before).Count(i => GearCatalog.Retired((Gear)i));
            double each = (double)N / (before - retiredCount), eachWeapon = each * (before - (int)Gear.Laser) / weaponKinds;
            double Expect(int i) => GearCatalog.IsSpecial((Gear)i) ? 0 : GearCatalog.IsWeapon((Gear)i) ? eachWeapon : each;
            Check(Enumerable.Range(0, kinds.Length).All(i => GearCatalog.Retired((Gear)i)
                      ? kinds[i] == 0
                      : Math.Abs(kinds[i] - Expect(i)) <= Expect(i) * 0.08),
                  "every gear equally likely, every weapon equally likely, no absorber/retired: " + string.Join(" ", kinds));
            Check(Loot.DropChance(EnemyKind.Runner, false, 1, 0, 0f) == 0.06f, "normal 6%");
            Check(Loot.DropChance(EnemyKind.Runner, false, 2, 0, 0f) == 1f, "first kill from wave 2 always drops");
            Check(Loot.DropChance(EnemyKind.Runner, true, 3, 1, 0f) == 0.35f, "elite 35%");
            Check(Loot.DropChance(EnemyKind.Warden, false, 10, 4, 0f) == 1f, "boss always");
            Check(Math.Abs(Loot.DropChance(EnemyKind.Runner, false, 5, 1, 2f) - 0.12f) < 1e-6, "lucky charm x2");
            Check(Loot.ChestRoll(5).Count == 2 && Loot.ChestRoll(10).Count == 3 && Loot.ChestRoll(25).Count == 4, "chest sizes 2 / 3 / 4");
            GearNumbers();
        }

        // Every gear at every rarity and enhancement: what the vault shows is what play uses.
        private static void GearNumbers()
        {
            Section("gear");
            var discount = typeof(Perks).GetMethod("Discount", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (Gear g in Enum.GetValues(typeof(Gear)))
                for (int enh = 0; enh <= GearCatalog.MaxEnhance; enh++)
                    foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
                    {
                        var v = new VaultData();
                        if (enh > 0) v.Enhance[g] = enh;
                        float val = v.ValueOf(g, r);
                        Check(val >= GearCatalog.Value(g, r) && (r == Rarity.Legendary || val == GearCatalog.Value(g, r)), $"{g} {r} +{enh}: only legendaries are enhanced");
                        if (r > 0) Check(GearCatalog.Value(g, r) >= GearCatalog.Value(g, r - 1), $"{g}: {r} isn't better than {r - 1}");
                        string shown = GearCatalog.FormatValue(g, val);
                        if (g == Gear.BigNet || g == Gear.TeslaCoil)
                            Check(shown == Mathf.RoundToInt(val).ToString(), $"{g} {r} +{enh}: vault says {shown}, play uses {Mathf.RoundToInt(val)}");
                        string fx = GearCatalog.Effect(g, r, val);
                        Check(!fx.Contains("{") && fx.Contains(shown), $"{g} {r} +{enh}: effect text '{fx}' doesn't show {shown}");
                        if (GearCatalog.IsActive(g)) Check(GearCatalog.Cooldown(g, r) == 0f, $"{g} {r} has a cooldown (it's used up instead)");
                        if (g == Gear.ToolBelt)
                        {
                            int cost = (int)discount.Invoke(null, new object[] { 10, val });
                            Check(cost >= 1 && cost <= 10, $"tool belt {r} +{enh}: a 10-coin wall costs {cost}");
                        }
                        if (g == Gear.SharkArmor) Check(val < 1f, $"shark armor {r} +{enh} blocks {val:P0} of all damage");
                    }
            Check(GearCatalog.FormatValue(Gear.Shockwave, GearCatalog.Value(Gear.Shockwave, Rarity.Rare)) == "1.8", "a rare shockwave shows its 1.8 s stun (not \"2\")");
        }

        // ---------- fortress weapons: slot, model, recharge, and the shot geometry ----------

        private static void WeaponChecks()
        {
            Section("weapons");
            var weapons = GearCatalog.All.Where(GearCatalog.IsWeapon).ToList();
            Check(weapons.Count == 23, $"{weapons.Count} weapons, expected 23");
            // The absorber takes 190-193 and the gatling 194-197: past the boss trophies (180-189), clear of the game's 0-102 and
            // every other weapon.
            var ids = weapons.SelectMany(g => GearCatalog.Rarities.Select(r => (int)WeaponItems.IdOf(g, r))).ToList();
            Check(WeaponItems.IdOf(Gear.Absorber, Rarity.Common) == 190 && WeaponItems.IdOf(Gear.Absorber, Rarity.Legendary) == 193 && ids.Distinct().Count() == ids.Count
                  && WeaponItems.IdOf(Gear.Gatling, Rarity.Common) == 194 && WeaponItems.IdOf(Gear.Gatling, Rarity.Legendary) == 197
                  && ids.All(i => i > 102 && (i < 180 || i > 189)), "weapon item ids collide (the absorber's 190-193, the gatling's 194-197)");
            AbsorberChecks();
            GatlingChecks();
            GunSpecChecks(weapons);
            // The first fourteen keep 200-255 (saves hold them); the rocket and the game's guns take 150-177.
            Check(weapons.Max(g => WeaponItems.IdOf(g, Rarity.Legendary)) == 255 && WeaponItems.IdOf(Gear.Laser, Rarity.Common) == 200 && WeaponItems.IdOf(Gear.LobsterClaw, Rarity.Legendary) == 255
                  && WeaponItems.IdOf(Gear.Rocket, Rarity.Common) == WeaponItems.SecondId && WeaponItems.IdOf(Gear.Bow, Rarity.Legendary) == 177, "weapon item ids moved");
            Check(weapons.Where(GearCatalog.IsGameGun).Select(WeaponItems.GameName).SequenceEqual(new[] { "pistol", "shotgun", "smg", "sniperrifle", "assaultrifle", "bow" }), "the game's guns' names");
            string Model(string n) => Path.Combine(Src, "Models", n + ".obj");
            foreach (var g in weapons)
            {
                Check(GearCatalog.SlotKind(g) == 3 && !GearCatalog.IsActive(g), $"{g} isn't in the weapon slot");
                Check(GearCatalog.IsGameGun(g) || File.Exists(Model(Weapons.ModelName(g))), $"{g}: no model {Weapons.ModelName(g)}.obj"); // the game's guns are their own
                Check(GearCatalog.IsGameGun(g) || File.Exists(Path.Combine(Src, "Icons", "Gear" + g + ".png")), $"{g}: no icon");
                Check(g == Gear.Laser || g == Gear.WaterCannon || g == Gear.Boomerang || g == Gear.Gatling || GearCatalog.IsGameGun(g) || GearCatalog.Cooldown(g, Rarity.Common) > 0f, $"{g} fires with no cooldown"); // the laser heats up, the cannon's tank runs dry, the gatling spins up; the game's guns have their own rate
                for (var r = Rarity.Rare; r <= Rarity.Legendary; r++)
                    Check(GearCatalog.Cooldown(g, r) <= GearCatalog.Cooldown(g, r - 1), $"{g}: {r} recharges slower than {r - 1}");
            }
            Check(File.Exists(Model("Shark")), "no Shark.obj for the shark whistle");
            Check(File.Exists(Model("RocketShell")), "no RocketShell.obj for the rocket in flight");
            foreach (var g in weapons.Where(GearCatalog.IsGameGun))
            {
                for (var r = Rarity.Rare; r <= Rarity.Legendary; r++) Check(GearCatalog.Value(g, r) > GearCatalog.Value(g, r - 1), $"{g}: {r} hits no harder than {r - 1}");
                // Never below the game's own gun, and growing with the wave once its share of the wave passes that.
                Check(WeaponItems.GunDamage(1f, g, 1f) == 1f && WeaponItems.GunDamage(2f, g, 1f) == 2f, $"{g}: a common at wave 1 isn't the game's gun");
                // The wave no longer scales player damage (0.1.7): a game gun is worth the gun, whatever the wave.
                Check(WeaponItems.GunDamage(1f, g, 1f) == 1f, $"{g}: a game gun's damage should not carry the wave any more");
            }
            foreach (var n in new[] { "HandNet", "HandNetBag", "NetFish" }) Check(File.Exists(Model(n)), $"no {n}.obj for the hand net");
            Check(Math.Abs(Weapons.LaserRamp(0f) - 1f) < 1e-4f && Math.Abs(Weapons.LaserRamp(Weapons.LaserRampSeconds) - Weapons.LaserMaxRamp) < 1e-4f
                  && Weapons.LaserRamp(99f) == Weapons.LaserMaxRamp && Weapons.LaserRamp(-1f) == 1f, "laser: x1 at first, x3 after 2 s on one fish, never more");
            Check(Math.Abs(Weapons.LaserRamp(Weapons.LaserRampSeconds, 5f) - 5f) < 1e-4f && Weapons.LaserRamp(0f, 5f) == 1f, "laser trait: the same 2 s up to the higher cap");
            ModChecks(weapons);
            DpsChecks();
            // The laser's colour: a common starts red, each rarity a colour up, its bite now (power, the ramp) carries it to the
            // next; only a legendary with everything goes white.
            float maxBite = Weapons.BeamMaxBite;
            Check(Weapons.BeamHeat(Rarity.Common, 1f, maxBite) == 0f && Weapons.BeamHeat(Rarity.Rare, 1f, maxBite) == 1f && Weapons.BeamHeat(Rarity.Legendary, 1f, maxBite) == 3f
                  && Math.Abs(Weapons.BeamHeat(Rarity.Legendary, maxBite, maxBite) - 4f) < 1e-4f && Weapons.BeamHeat(Rarity.Common, 3f, maxBite) < 1f, "laser colour by its bite");
            // ...and a legendary held on till it bites its hardest goes past white into the rainbow (Owen: "雷射超滿輸出變彩虹色"),
            // brighter the harder it bites; nothing else does, and nothing short of that.
            Check(Weapons.BeamShown(Rarity.Legendary, 1f, maxBite, true) == Weapons.RainbowHeat && Weapons.BeamShown(Rarity.Legendary, maxBite, maxBite, true) > Weapons.RainbowHeat + 0.9f
                  && Weapons.BeamShown(Rarity.Legendary, maxBite, maxBite, true) < Weapons.RainbowHeat + 1f && Weapons.BeamShown(Rarity.Legendary, maxBite, maxBite, false) < Weapons.RainbowHeat
                  && Weapons.BeamShown(Rarity.Epic, maxBite, maxBite, true) == Weapons.BeamHeat(Rarity.Epic, maxBite, maxBite), "the laser's rainbow: a legendary at its full ramp, only");
            // The hook's throw: worth more, hits harder; nothing worth still hits like the hook.
            Check(Weapons.FlingMult(0) == 1f && Weapons.FlingMult(400) > Weapons.FlingMult(10) && Weapons.FlingMult(20000) > Weapons.FlingMult(9000), "a thrown thing's damage by its worth");
            Vector3 a = new Vector3(1f, 2f, 3f), b = new Vector3(15f, 2f, 3f);
            float leg = Weapons.BoomerangLeg;
            Check((Weapons.BoomerangAt(a, b, 0f) - a).magnitude < 1e-3f && (Weapons.BoomerangAt(a, b, leg) - b).magnitude < 1e-3f
                  && (Weapons.BoomerangAt(a, b, 2f * leg) - a).magnitude < 1e-3f, "boomerang: out to the end, back to the thrower");
            bool between = true;
            for (float t = 0f; t <= 2f * leg; t += 0.01f) { var p = Weapons.BoomerangAt(a, b, t); between &= p.x >= a.x - 1e-3f && p.x <= b.x + 1e-3f; }
            Check(between, "boomerang never flies past its end or behind the thrower");
            Check(Math.Abs(Weapons.DistToSegment(new Vector3(5f, 3f, 3f), a, b) - 1f) < 1e-4f && Math.Abs(Weapons.DistToSegment(new Vector3(20f, 2f, 3f), a, b) - 5f) < 1e-4f
                  && Math.Abs(Weapons.DistToSegment(new Vector3(-2f, 2f, 7f), a, b) - 5f) < 1e-4f, "distance from a fish to a shot's line");
        }

        // The absorber: shop-only, locked behind the first boss, what it holds is each player's own and survives a save, and its
        // numbers (an item's worth, the trophies' moves, what it copies at) hold together.
        private static void AbsorberChecks()
        {
            Check(GearCatalog.IsSpecial(Gear.Absorber) && !GearCatalog.IsGameGun(Gear.Absorber) && GearCatalog.IsWeapon(Gear.Absorber) && !GearCatalog.Droppable.Contains(Gear.Absorber)
                  && GearCatalog.WeaponKinds.Where(GearCatalog.IsSpecial).SequenceEqual(new[] { Gear.Absorber, Gear.Gatling }), "the absorber: a weapon, not a game gun, never dropped");
            Check(!Loot.ForSale(Gear.Absorber, new VaultData { Best = Loot.SpecialBest - 1 }) && Loot.ForSale(Gear.Absorber, new VaultData { Best = Loot.SpecialBest })
                  && Loot.ForSale(Gear.Laser, new VaultData()), "the absorber is for sale once the first boss is beaten, the rest always");
            Check(Loot.ShopPrice(Gear.Absorber) == Siege.Money(Loot.SpecialPrice) && Loot.ShopPrice(Gear.Absorber) >= 10 * Loot.ShopPrice(Gear.Laser), "the absorber costs a special weapon's price");
            var v = new VaultData();
            v.Absorbed[7] = new StoredItem { Saved = new SavedItem { Exists = true, ItemID = 183, Weight = 2.5f }, Dead = true, Worth = 25000 };
            var host = VaultData.Read(v.Write());
            var client = VaultData.Read(v.Write(forClients: true));
            Check(host.AbsorbedOf(7) is StoredItem h && h.Id == 183 && h.Dead && h.Worth == 25000 && Math.Abs(h.Saved.Weight - 2.5f) < 1e-4f && host.AbsorbedOf(8) == null,
                  "what an absorber holds is lost or changed in a save");
            Check(client.AbsorbedOf(7) is StoredItem c && c.Id == 183 && c.Worth == 25000, "a client doesn't learn what an absorber holds");
            Check(Math.Abs(Weapons.ShotMult(0) - 1f) < 1e-4f && Math.Abs(Weapons.ShotMult(50) - 2f) < 1e-4f && Math.Abs(Weapons.ShotMult(350) - 4f) < 1e-4f
                  && Weapons.ShotMult(310000) > Weapons.ShotMult(15000), "an item shot by its worth: x2 at $50, x4 at $350, more for more");
            foreach (var k in EnemyCatalog.BossOrder)
                Check(Weapons.MoveSeconds(k) >= 2f && Weapons.MoveMult(k) > 0f, $"the absorber's {k} move: no cooldown or no damage");
            Check(Weapons.CopyRarity(Rarity.Common, Rarity.Legendary) == Rarity.Legendary && Weapons.CopyRarity(Rarity.Epic, Rarity.Rare) == Rarity.Epic, "a copied weapon fires at the rarer of the two");
        }

        // The gatling: shop-only once the wave 80 boss is beaten (Owen: "應該要過80關才能解鎖"), never dropped nor copied by an
        // absorber; its barrels speed it up, and the ones the game cuts out of its mesh to turn are exactly GatlingBarrels.obj.
        private static void GatlingChecks()
        {
            Check(GearCatalog.IsSpecial(Gear.Gatling) && !GearCatalog.Droppable.Contains(Gear.Gatling) && !GearCatalog.IsGameGun(Gear.Gatling) && Loot.UnlockBest(Gear.Gatling) == 80
                  && !Loot.ForSale(Gear.Gatling, new VaultData { Best = 79 }) && Loot.ForSale(Gear.Gatling, new VaultData { Best = 80 })
                  && Loot.ForSale(Gear.Absorber, new VaultData { Best = 79 }) && Loot.ShopPrice(Gear.Gatling) == Siege.Money(Loot.SpecialPrice), "the gatling: the smithy's once wave 80 is beaten");
            // The smithy sells active gear (K) too (Owen: 「鐵匠台那邊應該賣更多東西」): a common one into the vault, dearer the
            // further the save has got, never into a full vault; other gear isn't for sale. The game's explosives are found at
            // run time from its items (none without the game).
            {
                var fresh = new VaultData();
                Check(Loot.ShopPrice(Gear.Overclock, fresh) == Siege.Money(Loot.ActivePrice) && Loot.ActiveShopPrice(50) == Siege.Money(Loot.ActivePrice * 6)
                      && Loot.ActiveShopPrice(100) > Loot.ActiveShopPrice(50) && Loot.ShopPrice(Gear.Laser, fresh) == Loot.ShopPrice(Gear.Laser),
                      $"active gear's price: {Loot.ActiveShopPrice(0)} / {Loot.ActiveShopPrice(10)} / {Loot.ActiveShopPrice(50)} / {Loot.ActiveShopPrice(100)}");
                Check(GearCatalog.All.Where(GearCatalog.IsActive).All(g => Loot.Buyable(g, fresh)) && !Loot.Buyable(Gear.Gunpowder, fresh) && !Loot.Buyable(Gear.SharkArmor, fresh)
                      && !Loot.Buyable(Gear.Absorber, fresh) && Loot.Buyable(Gear.Rocket, fresh), "what the shop hands over");
                var full = new VaultData();
                foreach (var g in GearCatalog.All) full.Add(g, Rarity.Epic, 1);
                for (byte id = 1; full.SlotsUsed < full.StorageSlots && id < 200; id++) full.AddItem(new StoredItem { Saved = new SavedItem { Exists = true, ItemID = id } });
                Check(!Loot.Buyable(Gear.Decoy, full) && Loot.Buyable(Gear.Laser, full), "active gear bought into a full vault (a weapon goes to your hands)");
                Check(Loot.ShopItems.Count == 0, "explosives found without the game's items");
            }
            Check(Weapons.GatlingShots(0f) == Weapons.GatlingRate * Weapons.GatlingFloor && Weapons.GatlingShots(1f) == Weapons.GatlingRate && Weapons.GatlingShots(2f) == Weapons.GatlingRate
                  && Weapons.GatlingShots(0.5f) > Weapons.GatlingShots(0.4f), "the gatling: a quarter of its rate as the barrels start, all of it spun up");
            List<(Vector3 a, Vector3 b, Vector3 c)> Tris(string name)
            {
                var v = new List<Vector3>();
                var t = new List<(Vector3, Vector3, Vector3)>();
                foreach (var line in File.ReadLines(Path.Combine(Src, "Models", name + ".obj")))
                {
                    var q = line.Split(' ');
                    float F(int i) => float.Parse(q[i], System.Globalization.CultureInfo.InvariantCulture);
                    if (q[0] == "v") v.Add(new Vector3(-F(1), F(2), F(3))); // as Models.Parse: x flipped
                    else if (q[0] == "f")
                    {
                        var idx = q.Skip(1).Select(s => int.Parse(s.Split('/')[0]) - 1).ToList();
                        for (int k = 1; k + 1 < idx.Count; k++) t.Add((v[idx[0]], v[idx[k]], v[idx[k + 1]]));
                    }
                }
                return t;
            }
            var whole = Tris("Gatling");
            int cut = whole.Count(t => WeaponItems.GatlingBarrel(t.a) && WeaponItems.GatlingBarrel(t.b) && WeaponItems.GatlingBarrel(t.c));
            int across = whole.Count(t => new[] { t.a, t.b, t.c }.Count(WeaponItems.GatlingBarrel) is int n && n > 0 && n < 3);
            Check(cut > 0 && cut == Tris("GatlingBarrels").Count && across == 0, $"the gatling's barrels: {cut} triangles cut out, {Tris("GatlingBarrels").Count} turning, {across} half in");
        }

        // Each fortress weapon's own size, pose and muzzle in the hand (WeaponItems.Spec), its drop, and the shark whistle's
        // sharks (GUN-MODEL-AUDIT P0s: one 0.22 m pistol rule for everything, shots from a fixed point under the eyes, no
        // sharks ever made, the game's guns lying on the ground as nothing).
        private static void GunSpecChecks(List<Gear> weapons)
        {
            (Vector3 min, Vector3 max) Box(string name)
            {
                Vector3 lo = Vector3.one * 99f, hi = -lo;
                foreach (var line in File.ReadLines(Path.Combine(Src, "Models", name + ".obj")))
                {
                    var q = line.Split(' ');
                    if (q[0] != "v") continue;
                    float F(int i) => float.Parse(q[i], System.Globalization.CultureInfo.InvariantCulture);
                    var v = new Vector3(-F(1), F(2), F(3)); // as Models.Parse: x flipped
                    lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v);
                }
                return (lo, hi);
            }
            var ours = weapons.Where(g => !GearCatalog.IsGameGun(g)).ToList();
            Check(ours.Count == 17, $"{ours.Count} fortress weapons, expected 17");
            foreach (var g in ours)
            {
                var spec = WeaponItems.Spec(g);
                var (lo, hi) = Box(Weapons.ModelName(g));
                var size = hi - lo;
                // The art budget (CURRENT-DESIGN, GUN-UPGRADE-ROUND2): at most 1500 triangles and 6 materials a weapon.
                var lines = File.ReadLines(Path.Combine(Src, "Models", Weapons.ModelName(g) + ".obj")).ToList();
                int tris = lines.Where(l => l.StartsWith("f ")).Sum(l => l.Split(' ').Length - 3), mats = lines.Where(l => l.StartsWith("usemtl ")).Distinct().Count();
                Check(tris <= 1500 && mats <= 6, $"{g}: {tris} triangles, {mats} materials (budget 1500 / 6)");
                Check(spec.HasMuzzle && spec.Longest > 0.1f && spec.Longest <= Math.Max(size.x, Math.Max(size.y, size.z)) + 1e-3f, $"{g}: no spec, or held bigger than its model ({spec.Longest} m)");
                var m = spec.Muzzle;
                Check(m.x >= lo.x - 0.01f && m.x <= hi.x + 0.01f && m.y >= lo.y - 0.01f && m.y <= hi.y + 0.01f && m.z >= lo.z - 0.01f && m.z <= hi.z + 0.01f, $"{g}: muzzle {m} outside its model {lo}..{hi}");
                bool gun = spec.Kind == WeaponItems.Display.Pistol || spec.Kind == WeaponItems.Display.Long || spec.Kind == WeaponItems.Display.Heavy;
                if (gun) Check(m.z > hi.z - 0.18f && Math.Abs(m.x) < 0.02f, $"{g}: muzzle {m} isn't at the front of the barrel (front {hi.z})");
                float k = WeaponItems.HeldScale(size, spec);
                Check(Math.Abs(Math.Max(size.x, Math.Max(size.y, size.z)) * k - spec.Longest) < 1e-4f, $"{g}: held scale doesn't make it {spec.Longest} m");
                Check(-lo.z * k - WeaponItems.SlideForward(lo.z, k) <= WeaponItems.BackMax + 1e-4f, $"{g}: its back end reaches into the camera");
                Check(WeaponItems.DropLength(g, Rarity.Common) >= spec.Longest && WeaponItems.DropLength(g, Rarity.Epic) > WeaponItems.DropLength(g, Rarity.Rare), $"{g}: drop smaller than in the hand, or epic not larger");
            }
            float Len(Gear g) => WeaponItems.Spec(g).Longest;
            float Max(WeaponItems.Display k) => ours.Where(g => WeaponItems.Spec(g).Kind == k).Max(Len);
            float Min(WeaponItems.Display k) => ours.Where(g => WeaponItems.Spec(g).Kind == k).Min(Len);
            Check(Min(WeaponItems.Display.Heavy) > Max(WeaponItems.Display.Long) && Min(WeaponItems.Display.Long) > Max(WeaponItems.Display.Pistol)
                  && Min(WeaponItems.Display.Pistol) > WeaponItems.InHand.Longest, "pistols < long guns < heavy guns, all bigger than the old 0.22 m");
            Check(Len(Gear.Rocket) >= 2.5f * WeaponItems.InHand.Longest && Len(Gear.Gatling) >= 2.5f * WeaponItems.InHand.Longest, "the rocket and the gatling are big in the hand");
            Check(WeaponItems.DropLength(Gear.Rocket, Rarity.Common) > WeaponItems.DropLength(Gear.Laser, Rarity.Common), "a rocket on the ground is bigger than a laser");
            foreach (var g in new[] { Gear.Boomerang, Gear.SharkWhistle, Gear.Conch, Gear.PufferBomb })
                Check(WeaponItems.Spec(g).Kind != WeaponItems.Display.Pistol && WeaponItems.Spec(g).Euler != Vector3.zero, $"{g} is held like a pistol");
            Check(WeaponItems.Spec(Gear.Boomerang).Euler == WeaponItems.StandUp, "the boomerang in the hand isn't stood up like its hotbar icon");
            string weaponsSrc = File.ReadAllText(Path.Combine(Src, "Weapons.cs")) + File.ReadAllText(Path.Combine(Src, "Absorber.cs"));
            Check(!weaponsSrc.Contains("Muzzle(o, d)") && Regex.Matches(weaponsSrc, @"d \* 0\.6f \+ Vector3\.down \* 0\.25f").Count == 1,
                  "shots start at the shooter's muzzle (HeldMuzzle), the fixed point under the eyes only as the fallback");
            Check(weaponsSrc.Contains("HeldMuzzle(shooter"), "a friend's shot isn't drawn from their gun");
            // The game's guns on the ground: their own mesh, not a missing OBJ.
            foreach (var g in weapons)
            {
                string drop = Loot.DropModel(g);
                if (GearCatalog.IsGameGun(g)) Check(drop == null && !string.IsNullOrEmpty(WeaponItems.GameName(g)), $"{g}: its drop looks for {drop}.obj instead of the game's gun");
                else Check(drop != null && File.Exists(Path.Combine(Src, "Models", drop + ".obj")), $"{g}: no drop model {drop}.obj");
            }
            Check(File.ReadAllText(Path.Combine(Src, "Loot.cs")).Contains("WeaponItems.GameGunModel(g, spin)"), "a game gun's drop isn't drawn from its own mesh");
            // A rare-or-better weapon on the ground wears its rarity's look (RarityLook) like one in the hand.
            Check(File.ReadAllText(Path.Combine(Src, "Loot.cs")).Contains("WeaponItems.RarityLook(model, r)"), "a rare weapon on the ground is dressed as a common");
            // Icons are rendered mirrored, as Models.Parse flips x: the same side of a gun as the holder sees.
            Check(File.ReadAllText(Path.Combine(Program.Repo, "tools", "make_icons.py")).Contains("name in MIRROR"), "weapon icons aren't mirrored like the game's models");
            // The gatling's cluster carries a common's count of barrels (their dark bores sit proud of the muzzle plane).
            {
                var bores = new List<Vector3>();
                foreach (var line in File.ReadLines(Path.Combine(Src, "Models", "GatlingBarrels.obj")))
                {
                    var q = line.Split(' ');
                    if (q[0] != "v") continue;
                    float F(int i) => float.Parse(q[i], System.Globalization.CultureInfo.InvariantCulture);
                    var v = new Vector3(F(1), F(2), F(3));
                    if (v.z > 0.4425f && !bores.Any(b => (b - v).sqrMagnitude < 0.012f * 0.012f)) bores.Add(v);
                }
                Check(bores.Count == Weapons.GatlingBarrelBase, $"the gatling's cluster has {bores.Count} barrels, a common fires {Weapons.GatlingBarrelBase}");
            }
            // The shark whistle makes sharks (SharkTick had nothing to hunt with) that stay by their pool.
            Check(weaponsSrc.Contains("_sharks.Add(sh)"), "the shark whistle never makes a shark");
            Check(GearCatalog.Rarities.All(r => Weapons.SharkSpots(Vector3.zero, r).Length == (r == Rarity.Legendary ? 2 : 1)), "one shark, two for a legendary");
            Vector3 home = new Vector3(5f, 1f, 5f);
            var step = Weapons.SharkStep(home, home, home + new Vector3(10f, 0f, 0f), 14f, 1f);
            Check((step - (home + Vector3.right)).magnitude < 1e-4f, $"a shark swims toward its prey: {step}");
            var held = Weapons.SharkStep(home + new Vector3(13.5f, 0f, 0f), home, home + new Vector3(30f, 0f, 0f), 14f, 2f);
            Check(Math.Abs(new Vector2(held.x - home.x, held.z - home.z).magnitude - 14f) < 1e-3f, $"a shark stays within its sight of the pool: {held}");
            Check(Weapons.SharkLife.All(t => t > Weapons.SharkLeap + Weapons.SharkBiteEvery), "a shark lives long enough to bite more than once");
        }

        // Damage a second (Sim.Dps): within each damage role every weapon stays within x1.6 of the role's median (single-target
        // weapons judged on one fish, area weapons on the crowd, the game's guns on one fish), control weapons sit under the
        // single-target median, and the absorber, the final gun, out-hits everything at the same rarity and mods with its own
        // shots, without melting a boss faster than about the best other legendary.
        private static void DpsChecks()
        {
            var kinds = GearCatalog.WeaponKinds.Where(g => g != Gear.Absorber).ToList();
            var roles = kinds.Where(g => !GearCatalog.IsSpecial(g)).ToList(); // the gatling, a wave-80 reward, is judged on its own
            float Median(IEnumerable<float> xs) { var o = xs.OrderBy(x => x).ToList(); return o.Count % 2 == 1 ? o[o.Count / 2] : (o[o.Count / 2 - 1] + o[o.Count / 2]) / 2f; }
            foreach (var r in new[] { Rarity.Common, Rarity.Rare, Rarity.Epic, Rarity.Legendary })
                foreach (int l in new[] { 0, 5 })
                {
                    float Judged(Gear g) { var d = Sim.Dps(g, r, l); return Sim.RoleOf(g) == Sim.Role.Area ? d.crowd : d.one; }
                    foreach (var role in new[] { Sim.Role.Single, Sim.Role.Area, Sim.Role.Gun })
                    {
                        var members = roles.Where(g => Sim.RoleOf(g) == role).ToList();
                        float mid = Median(members.Select(Judged));
                        foreach (var g in members)
                            Check(Judged(g) >= mid / 1.6f && Judged(g) <= mid * 1.6f, $"{g} {r} Lv{l}: {Judged(g):0} a second, outside x1.6 of the {role} median {mid:0}");
                    }
                    // Across roles: control never meaningfully out-damages the single-target weapons, a single-target weapon never
                    // out-hits the area ones in a crowd, nor an area weapon the single-target ones on one fish.
                    float single = Median(roles.Where(g => Sim.RoleOf(g) == Sim.Role.Single).Select(g => Sim.Dps(g, r, l).one));
                    float area = Median(roles.Where(g => Sim.RoleOf(g) == Sim.Role.Area).Select(g => Sim.Dps(g, r, l).crowd));
                    foreach (var g in roles.Where(g => Sim.RoleOf(g) == Sim.Role.Control))
                        Check(Sim.Dps(g, r, l).one <= 1.2f * single, $"{g} {r} Lv{l}: a control weapon out-damages the single-target median ({Sim.Dps(g, r, l).one:0} > 1.2 x {single:0})");
                    foreach (var g in roles.Where(g => Sim.RoleOf(g) == Sim.Role.Single))
                        Check(Sim.Dps(g, r, l).crowd <= 1.6f * area, $"{g} {r} Lv{l}: a single-target weapon out-hits the area ones in a crowd ({Sim.Dps(g, r, l).crowd:0} > 1.6 x {area:0})");
                    foreach (var g in roles.Where(g => Sim.RoleOf(g) == Sim.Role.Area))
                        Check(Sim.Dps(g, r, l).one <= 1.6f * single, $"{g} {r} Lv{l}: an area weapon out-hits the single-target ones on one fish ({Sim.Dps(g, r, l).one:0} > 1.6 x {single:0})");
                    // The gatling out-pours every other weapon on one fish at the same rarity and mods (a wave-80 reward), not by
                    // miles, and stays under the absorber's own shots (below).
                    float best = roles.Max(g => Sim.Dps(g, r, l).one), gat = Sim.Dps(Gear.Gatling, r, l).one;
                    Check(gat > best && gat <= 2.6f * best, $"gatling {r} Lv{l}: {gat:0} a second on one fish, against the best other weapon's {best:0}");
                    var own = Sim.AbsorberOwn(r, l);
                    Check(own.one > kinds.Max(g => Sim.Dps(g, r, l).one) && own.crowd > kinds.Max(g => Sim.Dps(g, r, l).crowd),
                          $"absorber {r} Lv{l}: its own shots ({own.one:0} | {own.crowd:0}) don't beat every other weapon");
                }
            float bestBoss = kinds.Max(g => Sim.Dps(g, Rarity.Legendary, 5).one);
            Check(Sim.AbsorberDps(Rarity.Legendary, 5, boss: true).one <= 1.6f * bestBoss, "the absorber melts bosses far faster than the best other legendary");
            // Bosses last (Owen: "王血太少"): the wave-50 one isn't down in seconds to a legendary with its mods at 5, and the first
            // one doesn't outlast its crash timer for a newcomer's common gun.
            float wave50 = Sim.BossHp(50) / Sim.BestSingle(Rarity.Legendary, 5), wave10 = Sim.BossHp(10) / Sim.BestSingle(Rarity.Common, 0);
            Check(wave50 >= 20f && wave10 <= Bosses.CrashAfter / 2f, $"a boss lasts {wave50:0.0} s at wave 50 (legendary Lv5), {wave10:0.0} s at wave 10 (common Lv0), solo");
            Check(Enumerable.Range(1, 99).All(w => FortressPlugin.BossHpMult(w + 1) >= FortressPlugin.BossHpMult(w)), "boss health doesn't keep growing with the wave");
            Check(Weapons.AbsorberBoost > 1f, "the absorber isn't boosted against fish");
        }

        // Each player's weapon mods: four lines, no top level, every one doing something, and what the smithy shows is what play uses.
        private static void ModChecks(List<Gear> weapons)
        {
            // Prices: as before to Lv5, then every level dearer than the last until they can't be paid at all.
            Check(Enumerable.Range(0, 5).Select(WeaponMods.Cost).SequenceEqual(new[] { 8, 15, 25, 40, 60 }), "mod prices to Lv5 changed");
            for (int l = 1; l <= 200; l++)
                Check(WeaponMods.Cost(l) > WeaponMods.Cost(l - 1) || WeaponMods.Cost(l) == int.MaxValue, $"mod price Lv{l} -> Lv{l + 1} isn't dearer than the one before");
            // What a level does stays sane however high it goes: damage keeps growing, the rest tapers to a bound.
            Check(WeaponMods.PowerMult(0) == 1f && WeaponMods.TimeMult(0) == 1f && WeaponMods.ReachMult(0) == 1f && WeaponMods.Soft(0) == 0f && WeaponMods.Soft(5) == 5f
                  && WeaponMods.TimeMult(5) < 0.8f && Math.Abs(WeaponMods.PowerMult(5) - 1.75f) < 1e-4f, "mods at 0 change nothing; Lv5 is much stronger");
            for (int l = 1; l <= 200; l++)
            {
                Check(WeaponMods.PowerMult(l) > WeaponMods.PowerMult(l - 1) && WeaponMods.PowerMult(l, Gear.Conch) > WeaponMods.PowerMult(l - 1, Gear.Conch), $"power at Lv{l}");
                Check(WeaponMods.TimeMult(l) <= WeaponMods.TimeMult(l - 1)
                      && (WeaponMods.TimeMult(l - 1) / WeaponMods.TimeMult(l) >= 1.0099f || WeaponMods.TimeMult(l) <= 0.0101f),
                      $"fire rate at Lv{l}: {WeaponMods.TimeMult(l)} (every level at least +3% rate until the floor)");
                Check(WeaponMods.ReachMult(l) > WeaponMods.ReachMult(l - 1) && WeaponMods.ReachMult(l) < 100f, $"range at Lv{l}: {WeaponMods.ReachMult(l)}");
                Check(WeaponMods.Soft(l) > WeaponMods.Soft(l - 1) && WeaponMods.Soft(l) < 15f, $"trait levels at Lv{l}: {WeaponMods.Soft(l)}");
                Check(WeaponMods.MagAdd(30, l) > 0, $"magazine at Lv{l}: +{WeaponMods.MagAdd(30, l)}");
            }
            Check(WeaponMods.MagAdd(2, 1) == 1 && WeaponMods.MagAdd(30, 5) == 45 && WeaponMods.MagAdd(0, 0) == 0, "magazine: at least a round a level, a fifth of it on a big one");
            // A high level survives the file (and is bought with the low byte of the level it was bought at: VaultRequest.Count).
            var hv = new VaultData { Scales = int.MaxValue - 1 };
            hv.Mods[(9, Gear.Laser, WeaponMods.Rate)] = 300;
            Check(VaultData.Read(hv.Write()).ModOf(9, Gear.Laser, WeaponMods.Rate) == 300, "a Lv300 mod doesn't survive the file");
            // A file from before the range line: the boomerang's, conch's, water cannon's and absorber's trait was their range.
            var mig = VaultData.Read("mod 5 Boomerang 2 3\nmod 5 Rocket 2 4\nmod 6 Absorber 2 2");
            Check(mig.ModOf(5, Gear.Boomerang, WeaponMods.Reach) == 3 && mig.ModOf(5, Gear.Boomerang, WeaponMods.Trait) == 0 && mig.ModOf(5, Gear.Rocket, WeaponMods.Trait) == 4
                  && mig.ModOf(6, Gear.Absorber, WeaponMods.Reach) == 2 && VaultData.Read(mig.Write()).Write() == mig.Write(), "old range traits didn't move to the range line:\n" + mig.Write());
            Check(VaultData.Read("modv 2 0\nmod 5 Boomerang 2 3").ModOf(5, Gear.Boomerang, WeaponMods.Trait) == 3, "a new file's boomerang trait got moved");
            // Every fortress weapon reaches somewhere, and the claw catches what's along its line before what's beside you.
            foreach (var g in weapons.Where(w => !GearCatalog.IsGameGun(w))) Check(Weapons.RangeOf(g) > 0f, $"{g}: no range");
            Vector3 eye = Vector3.zero, end = new Vector3(0f, 0f, Weapons.ClawRange);
            Check(Weapons.ClawPick(eye, end, new List<Vector3> { new Vector3(2f, 0f, 1.5f), new Vector3(0.5f, 0f, 9f) }) == 1
                  && Weapons.ClawPick(eye, end, new List<Vector3> { new Vector3(2f, 0f, 1.5f), new Vector3(6f, 0f, 9f) }) == 0
                  && Weapons.ClawPick(eye, end, new List<Vector3> { new Vector3(0f, 0f, -2f), new Vector3(0.3f, 0f, 14f) }) == -1
                  && Weapons.ClawPick(eye, end, new List<Vector3> { new Vector3(0.3f, 0f, 8f), new Vector3(-0.2f, 0f, 4f) }) == 1, "the claw's catch");
            foreach (var g in weapons)
            {
                for (int t = 0; t < WeaponMods.Tracks; t++)
                    Check(WeaponMods.Has(g, t) == !(g == Gear.Bow && t == WeaponMods.Magazine), $"{g}: line {t} {(WeaponMods.Has(g, t) ? "shouldn't" : "should")} be there");
                if (GearCatalog.IsGameGun(g)) continue;
                Check(WeaponMods.TraitStep(g) > 0f, $"{g}: its trait does nothing");
                // The card and the HUD show the shooter's own power and speed.
                var v = new VaultData();
                v.Mods[(9, g, WeaponMods.Power)] = 2; v.Mods[(9, g, WeaponMods.Rate)] = 5;
                Check(Math.Abs(v.WeaponValue(9, g, Rarity.Epic) - GearCatalog.Value(g, Rarity.Epic) * 1.3f) < 1e-4f && v.WeaponValue(8, g, Rarity.Epic) == GearCatalog.Value(g, Rarity.Epic), $"{g}: power isn't the shooter's own");
                string fx = v.EffectFor(9, g, Rarity.Rare), shown = GearCatalog.FormatValue(g, v.WeaponValue(9, g, Rarity.Rare));
                Check(fx.Contains(shown), $"{g}: card '{fx}' doesn't show {shown}");
                if (GearCatalog.Cooldown(g, Rarity.Rare) > 0f)
                    Check(fx.Contains((GearCatalog.Cooldown(g, Rarity.Rare) * WeaponMods.TimeMult(5)).ToString("0.#")), $"{g}: card '{fx}' doesn't show the faster cooldown");
            }
            // "+N" on a tile, the hotbar and a name: a weapon's is that player's mods, gear's its enhancement (legendaries only).
            var pv = new VaultData();
            pv.Mods[(9, Gear.Rocket, 0)] = 1; pv.Mods[(9, Gear.Rocket, 2)] = 2; pv.Enhance[Gear.Rebar] = 4;
            Check(pv.PlusOf(Gear.Rocket, Rarity.Common, 9) == 3 && pv.PlusOf(Gear.Rocket, Rarity.Common, 8) == 0 && pv.PlusOf(Gear.Rebar, Rarity.Legendary, 9) == 4
                  && pv.PlusOf(Gear.Rebar, Rarity.Epic, 9) == 0 && pv.LabelOf(Gear.Rocket, Rarity.Rare, 9).EndsWith(" +3</color>") && !pv.LabelOf(Gear.Rocket, Rarity.Rare, 8).Contains("+"),
                  "+N: a weapon's is the player's mods, gear's its enhancement");
            // The game's guns have no power line: bullets work on the shooter's machine, the host multiplies by rarity alone.
            var gv = new VaultData();
            gv.Mods[(9, Gear.Smg, WeaponMods.Bullets)] = 5;
            Check(gv.WeaponValue(9, Gear.Smg, Rarity.Rare) == GearCatalog.Value(Gear.Smg, Rarity.Rare), "a game gun's bullets mod counted twice (as power too)");
        }

        // The stall's helpers: ten times as many, as fast and as much carried as before, each step dearer than the last.
        // ---------- what keeps a late wave from lagging (the playtest: 「超當」「破音」) ----------

        private static void Performance()
        {
            Section("performance");
            // Sounds (Fx.MayPlay): one clip at most every SameGap, at most PerWindow starting in any Window.
            float t0 = 5000f;
            Check(Fx.MayPlay("perfA", t0) && !Fx.MayPlay("perfA", t0 + Fx.SameGap * 0.5f) && Fx.MayPlay("perfA", t0 + Fx.SameGap * 1.01f), "the same clip twice within 60 ms");
            float t1 = t0 + 10f;
            int started = Enumerable.Range(0, 40).Count(i => Fx.MayPlay("perfB" + i, t1));
            Check(started == Fx.PerWindow, $"{started} different sounds started at once (at most {Fx.PerWindow})");
            Check(Fx.MayPlay("perfC", t1 + Fx.Window), "no sound after the window");
            // Fewer effects (WeaponFx.Thin): two in five, a lone one at that chance; everything with the setting off.
            Check(WeaponFx.Thin(10, 0.99f, true) == 4 && WeaponFx.Thin(1, 0.3f, true) == 1 && WeaponFx.Thin(1, 0.5f, true) == 0 && WeaponFx.Thin(7, 0.5f, false) == 7,
                  "fewer effects thins the wrong amount");
            FxChecks();
        }

        // The effects stay readable and bounded (FX-ART-REPORT): a blast's flash never outgrows BlastCoreMax while its ring
        // is the real radius; a boss flash's halo stops at HaloMax and never rounds the camera; the water jet's drops live
        // long enough to reach what they hit; a rocket's explosion is drawn once (the host's Blast), its beam's foot follows
        // the gun, and the absorber's shots start at the muzzle like the weapons'.
        private static void FxChecks()
        {
            foreach (float r in new[] { 0.5f, 2f, 4.5f, 12f })
            {
                var look = Fx.BlastLook(r);
                Check(look.flash <= Fx.BlastCoreMax && look.flash >= 0.8f && look.ring == Math.Max(0.6f, r) && look.sparks <= 32 && look.smoke <= 10, $"blast of {r} m: flash {look.flash}, ring {look.ring}, {look.sparks} sparks, {look.smoke} smoke");
            }
            Check(Fx.BlastLook(12f).flash == Fx.BlastCoreMax && Fx.BlastLook(1f).flash < Fx.BlastLook(2f).flash, "a blast's flash doesn't grow with the radius, then stop");
            Check(BossVfx.HaloSize(9f) == BossVfx.HaloMax && BossVfx.HaloSize(2f) == 2f && BossVfx.Room(0.3f) < 0.5f && BossVfx.Room(3f) > BossVfx.HaloMax, "a flash's halo isn't capped, or rounds the camera");
            Check(Weapons.WaterJetLife(2f) == 0.25f && Weapons.WaterJetLife(8f) == 0.5f && Weapons.WaterJetLife(40f) == 0.7f && Weapons.WaterJetDrops <= 12 && Weapons.RocketPuffEvery >= 0.02f, "the water jet / rocket trail aren't bounded");
            string weapons = File.ReadAllText(Path.Combine(Src, "Weapons.cs")), fx = File.ReadAllText(Path.Combine(Src, "Fx.cs"));
            string rocket = weapons.Substring(weapons.IndexOf("private IEnumerator RocketView"));
            rocket = rocket.Substring(0, rocket.IndexOf("private IEnumerator", 10));
            Check(!rocket.Contains("Explosion") && !rocket.Contains("Fx.Sound"), "the rocket draws its own explosion on top of the host's Blast");
            Check(weapons.Contains("v.Muzzle()") && weapons.Contains("v.Muzzle = muzzle"), "the beam's foot doesn't follow the gun between host updates");
            Check(fx.Contains("e.From = Weapons.Instance.Origin(e)") && fx.Contains("Explosion(e.To, blastRadius)") && !fx.Contains("Vector3.up * 15f"), "turret blasts still a ball; the absorber's shots not from the muzzle; the death pillar in the air");
            Check(fx.Contains("WeaponFx.Line(\"HarpoonRope\"") && Regex.IsMatch(fx, @"_tracer = BossVfx\.Glow"), "the harpoon has no rope; the turret tracers don't glow");
        }

        private static void HelperChecks()
        {
            Section("helpers");
            Check(Helpers.MaxHelpers == 10 && Helpers.HumanHelpers == 5 && Helpers.Speed(Helpers.MaxLevel) >= 25f && Helpers.Basket(Helpers.MaxLevel) >= 60, "helpers: 10 max, 5 villagers then 5 seagulls, about 27 m/s, about 60 fish a trip");
            Check(Helpers.Speed(4) == 5.4f && Helpers.Basket(4) >= 6, "a save's Lv4 helpers are no worse than before");
            for (int i = 1; i < Helpers.MaxHelpers; i++) Check(Helpers.HirePrice(i) > Helpers.HirePrice(i - 1), $"helper {i + 1} isn't dearer than {i}");
            for (int l = 1; l < Helpers.MaxLevel; l++) Check(Helpers.UpgradePrice(l) > Helpers.UpgradePrice(l - 1), $"helper upgrade Lv{l + 1} isn't dearer");
            Check(Siege.Money(Helpers.HirePrice(Helpers.MaxHelpers - 1)) > 0 && Siege.Money(Helpers.UpgradePrice(Helpers.MaxLevel - 1)) > 0, "the dearest helper prices still fit");
            var v = new VaultData { Helpers = Helpers.MaxHelpers, HelperSpeed = Helpers.MaxLevel, HelperLoad = Helpers.MaxLevel };
            Check(Helpers.PriceOf(0, v) < 0 && Helpers.PriceOf(1, v) < 0 && Helpers.PriceOf(2, v) < 0, "nothing past the top");
            Console.WriteLine($"  hire: $" + string.Join(" / ", new[] { 1, 2, 5, 10, 20, 30 }.Select(n => Siege.Money(Helpers.HirePrice(n - 1)).ToString("N0"))) + $"; a level: $" + string.Join(" / ", new[] { 1, 5, 10, 20, 30, 40 }.Select(l => Siege.Money(Helpers.UpgradePrice(l - 1)).ToString("N0"))));
        }

        // ---------- getting out of what you built (Unstuck): the rule, where it looks, and what it takes ----------

        private static void PlayerChecks()
        {
            Section("players");
            Check(!Unstuck.Stuck(false, true, 0f, 3f, 5, true), "a corner (five ways shut) counts as stuck");
            Check(Unstuck.Stuck(false, true, 0.1f, Unstuck.StuckAfter, 6, true), "hemmed in by pieces, pushing and going nowhere, isn't stuck");
            Check(!Unstuck.Stuck(false, true, 0.1f, Unstuck.StuckAfter, 8, false), "shut in by rocks alone (no piece) counts: that's F4's");
            Check(!Unstuck.Stuck(false, true, 0.5f, 5f, 8, true) && !Unstuck.Stuck(false, false, 0f, 5f, 8, true), "moving, or not pushing, counts as stuck");
            Check(Unstuck.Stuck(true, false, 0f, 0f, 0, false), "inside a piece isn't stuck");
            var spots = Unstuck.Candidates(new Vector2(1f, 0f), true);
            Check(spots.Count == 12 * 11 + 7 * 12 && spots[0].Through && Vector2.Distance(spots[0].Offset, new Vector2(0.75f, 0f)) < 1e-4f
                  && spots.Skip(12 * 11).All(s => !s.Through) && Unstuck.Candidates(Vector2.up, false).Count == 7 * 12, $"where it looks: {spots.Count} spots");
            int ways = spots.Skip(12 * 11).Take(12).Select(s => Mathf.RoundToInt(Mathf.Atan2(s.Offset.y, s.Offset.x) * Mathf.Rad2Deg)).Distinct().Count();
            Check(ways == 12, $"a ring looks {ways} ways, not twelve");

            // A made-up fort: boxes (min, max); the ground at 0 on the island (|x|, |z| < 20), the sea from x = `sea` on.
            const float r = 0.35f, h = 1.9f;
            var boxes = new List<(Vector3 lo, Vector3 hi)>();
            float sea = 100f;
            Vector3? Ground(Vector3 from) => Mathf.Abs(from.x) < 20f && Mathf.Abs(from.z) < 20f && from.x < sea ? new Vector3(from.x, 0f, from.z) : (Vector3?)null;
            bool Room(Vector3 f) => !boxes.Any(b => f.x + r > b.lo.x && f.x - r < b.hi.x && f.z + r > b.lo.z && f.z - r < b.hi.z && f.y + h > b.lo.y && f.y < b.hi.y);
            bool InWay(Vector3 a, Vector3 c) => boxes.Any(b => Enumerable.Range(0, 41).Select(i => Vector3.Lerp(a, c, i / 40f))
                                                    .Any(p => p.x > b.lo.x && p.x < b.hi.x && p.z > b.lo.z && p.z < b.hi.z && p.y > b.lo.y && p.y < b.hi.y));
            Vector3? Find(Vector2 dir, bool through) => Unstuck.FindFree(Vector3.zero, dir, through, h / 2f, Ground, Room, InWay);
            // Shut in by a cell of walls (3 m apart, 0.3 thick): out through the one you face.
            boxes.AddRange(new[] { (new Vector3(1.35f, 0f, -1.65f), new Vector3(1.65f, 3f, 1.65f)), (new Vector3(-1.65f, 0f, -1.65f), new Vector3(-1.35f, 3f, 1.65f)),
                                   (new Vector3(-1.65f, 0f, 1.35f), new Vector3(1.65f, 3f, 1.65f)), (new Vector3(-1.65f, 0f, -1.65f), new Vector3(1.65f, 3f, -1.35f)) });
            var east = Find(new Vector2(1f, 0f), true);
            Check(east is Vector3 e && e.x > 1.65f + r && Mathf.Abs(e.z) < 0.01f, $"shut in, facing east: out at {east}");
            sea = 1.8f; // the sea just past the wall you face: out another way, on land
            var land = Find(new Vector2(1f, 0f), true);
            Check(land is Vector3 l && l.x < sea && Room(l) && (Mathf.Abs(l.x) > 1.65f + r || Mathf.Abs(l.z) > 1.65f + r), $"shut in, the sea past the wall you face: out at {land}");
            sea = 100f;
            boxes.Clear();
            boxes.Add((new Vector3(-0.7f, 0f, -0.7f), new Vector3(0.7f, 1.6f, 0.7f))); // inside a turret: the nearest room
            var turret = Find(new Vector2(0f, 1f), false);
            Check(turret is Vector3 t && Room(t) && t.magnitude <= 1.11f, $"inside a turret: out at {turret}");
            boxes.Clear(); // an open field: nothing to go through, so the nearest room, a step along
            var open = Find(new Vector2(1f, 0f), true);
            Check(open is Vector3 o && Vector3.Distance(o, new Vector3(0.7f, 0f, 0f)) < 1e-3f, $"open field: {open}");
            boxes.Add((new Vector3(-30f, 0f, -30f), new Vector3(30f, 3f, 30f))); // no room anywhere
            Check(Find(Vector2.up, true) == null, "no room anywhere still found a spot");
        }

        // ---------- every Harmony patch points at a method the game has (one wrong target and the mod doesn't load) ----------

        private static void Patches()
        {
            Section("patches");
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            int n = 0;
            foreach (var t in typeof(Loot).Assembly.GetTypes())
            {
                var cls = Target(t.GetCustomAttributes(false), (null, null, null));
                var perMethod = t.GetMethods(All | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes(false).Any(IsPatch))
                                 .Select(m => Target(m.GetCustomAttributes(false), cls)).ToList();
                if (perMethod.Count == 0 && !t.GetCustomAttributes(false).Any(IsPatch)) continue;
                foreach (var (type, name, kind) in perMethod.Count > 0 ? perMethod : new List<(Type, string, string)> { cls })
                {
                    n++;
                    bool found = type != null && name != null && (kind == "Getter" ? type.GetProperty(name, All)?.GetGetMethod(true) != null
                                 : kind == "Setter" ? type.GetProperty(name, All)?.GetSetMethod(true) != null
                                 : type.GetMethods(All).Any(m => m.Name == name));
                    Check(found, $"{t.Name} patches {type?.Name}.{name} ({kind ?? "method"}), which the game doesn't have");
                }
            }
            Console.WriteLine($"  {n} patch targets found in the game");
            Check(typeof(RigidbodySync).GetProperty("IsSendingUpdates")?.GetSetMethod(true) != null, "RigidbodySync.IsSendingUpdates has no setter: friends won't see fish we carry (Siege.Synced)");
        }

        private static bool IsPatch(object a) => a.GetType().Name == "HarmonyPatch";

        // ---------- the island: what players walk to stands on open ground, reachable from the pier ----------

        private static void IslandLayout()
        {
            Section("island");
            // The lighthouse's ladder: W climbs when you face it, S and facing away go down, nothing hangs; walking past it or
            // standing on the gallery doesn't grab it. The model has it where the code climbs it, clear of the gallery.
            float foot = 0.2f, deck = Ladder.DeckY;
            Check(Ladder.Way(1f, 0.9f, true, foot) == 1 && Ladder.Way(1f, 0.3f, true, foot) == 0 && Ladder.Way(-1f, 0.9f, true, foot) == 0, "the ladder from the ground: W facing it climbs, walking past or backing off doesn't");
            Check(Ladder.Way(0f, 0.9f, false, 2f) == 0 && Ladder.Way(1f, 0.9f, false, 2f) == 1 && Ladder.Way(1f, -0.9f, false, 2f) == -1 && Ladder.Way(-1f, 0.9f, false, 2f) == -1, "on the ladder: hang, up facing it, down facing away or with S");
            Check(Ladder.Way(1f, 0.9f, true, deck) == 0, "standing on the gallery doesn't grab the ladder again");
            var core = File.ReadLines(Path.Combine(Src, "Models", "Core.obj")).Where(l => l.StartsWith("v ")).Select(l => l.Split(' ')).Select(q => new Vector3(-float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(q[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(q[3], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            Check(core.Any(c => Math.Abs(c.x - Ladder.FaceX) < 0.05f && c.y > deck + 0.5f) && core.Where(c => Math.Abs(c.y - deck) < 0.01f).Max(c => new Vector2(c.x, c.z).magnitude) is float r && Math.Abs(r - Ladder.DeckR) < 0.03f,
                  "Core.obj: the ladder at Ladder.FaceX up past the gallery, whose floor is at DeckY out to DeckR");
            Check(Ladder.FaceX - Ladder.Off + 0.6f < -Ladder.DeckR, "climbing, a player 0.6 m round clears the gallery's edge");
            // Triangles of the island model in the game's frame (the loader flips X).
            var v = new List<Vector3>();
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string mat = "";
            int groundDown = 0, ground = 0;
            var groundMats = new HashSet<string> { "Sand", "Grass", "IslPath", "IslWetSand", "IslTileDark", "IslGrout" };
            foreach (var line in File.ReadLines(Path.Combine(Src, "Models", "FortressIsland.obj")))
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "v") v.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                else if (p[0] == "usemtl") mat = p[1];
                else if (p[0] == "f")
                {
                    var idx = p.Skip(1).Select(q => int.Parse(q.Split('/')[0]) - 1).ToArray();
                    for (int i = 1; i < idx.Length - 1; i++) tris.Add((v[idx[0]], v[idx[i]], v[idx[i + 1]]));
                    if (groundMats.Contains(mat))
                    {
                        // The loader mirrors x and reverses the winding, so in this (mirrored) frame a face that ends up facing
                        // up in the game has a downward cross product here.
                        var (a0, b0, c0) = (v[idx[0]], v[idx[1]], v[idx[2]]);
                        float ny = (b0.z - a0.z) * (c0.x - a0.x) - (b0.x - a0.x) * (c0.z - a0.z);
                        ground++;
                        if (ny > 1e-9f) groundDown++;
                    }
                }
            }
            // The ground is one open sheet: faces turned upside down are invisible from above and can't be stood on
            // (raycasts and the player's feet go straight through: people "fell under the ground").
            Check(ground > 0 && groundDown == 0, $"{groundDown} of {ground} ground faces face down");
            // Every surface straight above or below (x, z), lowest first.
            List<float> Hits(float x, float z)
            {
                var hs = new List<float>();
                foreach (var (a, b, c) in tris)
                {
                    if (Math.Min(a.x, Math.Min(b.x, c.x)) > x || Math.Max(a.x, Math.Max(b.x, c.x)) < x || Math.Min(a.z, Math.Min(b.z, c.z)) > z || Math.Max(a.z, Math.Max(b.z, c.z)) < z) continue;
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Math.Abs(d) < 1e-9f) continue;
                    float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d, l3 = 1f - l1 - l2;
                    if (l1 < -1e-5f || l2 < -1e-5f || l3 < -1e-5f) continue;
                    hs.Add(l1 * a.y + l2 * b.y + l3 * c.y);
                }
                hs.Sort();
                return hs;
            }
            // The top surface: what the game's ray from above lands on. A rock or a tree in the way shows up as a jump in it.
            float Top(float x, float z) { var h = Hits(x, z); return h.Count > 0 ? h[h.Count - 1] : float.NaN; }
            // A spot people stand at: flat, at beach level, within `r`.
            void Spot(string what, Vector3 at, float r)
            {
                var tops = new List<float>();
                for (float dx = -r; dx <= r + 1e-3f; dx += 0.5f)
                    for (float dz = -r; dz <= r + 1e-3f; dz += 0.5f)
                        if (dx * dx + dz * dz <= r * r) tops.Add(Top(at.x + dx, at.z + dz));
                Check(tops.All(y => !float.IsNaN(y)) && tops.Max() - tops.Min() < 0.6f && tops.Min() > 0.3f && tops.Max() < 2.5f,
                      $"{what}: not flat open ground (top surface {tops.Min():0.0} to {tops.Max():0.0} m)");
            }
            // Walking from a to b: each step lands on a surface at most 0.6 m up or down, with 2 m of headroom over it
            // (a tree's leaves overhead are fine, a rock isn't).
            void Walk(string what, Vector3 a, Vector3 b)
            {
                float last = Top(a.x, a.z);
                for (float t = 0f; t <= 1f; t += 0.25f / Math.Max(0.25f, (b - a).magnitude))
                {
                    var p = Vector3.Lerp(a, b, t);
                    var h = Hits(p.x, p.z);
                    int i = h.FindLastIndex(y => y <= last + 0.6f);
                    bool ok = i >= 0 && h[i] >= last - 0.6f && (i == h.Count - 1 || h[i + 1] - h[i] > 2f);
                    if (!ok) { Check(false, $"{what}: blocked near {p.x:0.0}, {p.z:0.0} (from {last:0.0} m; surfaces {string.Join(" / ", h.Select(y => y.ToString("0.0")))})"); return; }
                    last = h[i];
                }
            }
            Vector3 vault = FortressIsland.VaultLocal, ready = FortressIsland.ReadyLocal, pier = new Vector3(0.5f, 0f, -24f);
            Vector3 market = FortressIsland.MarketLocal, smithy = FortressIsland.SmithyLocal;
            // A station's front, `d` metres out along its yaw (Quaternion.Euler is native, so by hand).
            Vector3 Front(Vector3 at, float yawDeg, float d) { float y = yawDeg * Mathf.Deg2Rad; return at + new Vector3(d * (float)Math.Sin(y), 0f, d * (float)Math.Cos(y)); }
            Vector3 door = Front(vault, FortressIsland.VaultYaw, 2.6f), stall = Front(market, FortressIsland.MarketYaw, 2.4f), forge = Front(smithy, FortressIsland.SmithyYaw, 2.6f);
            Spot("quartermaster's shed", vault, 1.2f);
            Spot("vault door", door, 1f);
            Spot("fish stall", market, 1.5f);
            Spot("in front of the stall", stall, 1f);
            Spot("smithy", smithy, 1.8f);
            Spot("in front of the smithy", forge, 1f);
            Spot("lighthouse keeper", FortressIsland.KeeperLocal, 0.6f);
            Spot("in front of the lighthouse door", ready, 1.8f);
            Spot("record board", FortressIsland.BoardLocal, 0.9f);
            Spot("respawn spot", FortressIsland.RespawnLocal, 1f);
            Walk("respawn to the vault door", FortressIsland.RespawnLocal, new Vector3(FortressIsland.RespawnLocal.x, 0f, door.z));
            Walk("respawn to the vault door", new Vector3(FortressIsland.RespawnLocal.x, 0f, door.z), door);
            Walk("pier to the stall", pier, new Vector3(pier.x, 0f, stall.z));
            Walk("pier to the stall", new Vector3(pier.x, 0f, stall.z), stall);
            Walk("pier to the smithy", pier, new Vector3(pier.x, 0f, forge.z));
            Walk("pier to the smithy", new Vector3(pier.x, 0f, forge.z), forge);
            Walk("pier to the stall", pier, new Vector3(pier.x, 0f, stall.z));
            Walk("pier to the stall", new Vector3(pier.x, 0f, stall.z), stall);
            Walk("pier to start circle", pier, ready);
            // The tide pools (decor file): on the south-west beach, above the waterline.
            var pools = File.ReadLines(Path.Combine(Src, "Models", "FortressIsland.decor.txt")).Where(l => l.StartsWith("Pool ")).Select(l => l.Split(' ')).ToList();
            Check(pools.Count >= 3, $"only {pools.Count} tide pools in the decor file");
            foreach (var pp in pools)
            {
                float px = float.Parse(pp[1], inv), py = float.Parse(pp[2], inv), pz = float.Parse(pp[3], inv);
                float ang = (float)(Math.Atan2(pz, px) * 180.0 / Math.PI + 360.0) % 360f;
                Check(Math.Abs(ang - FortressIsland.GapAngles[1]) < 25f && py > -1.0f, $"tide pool at {px:0.0}, {pz:0.0} (y {py:0.0}) not on the south-west beach above the water");
            }
            Console.WriteLine($"  {tris.Count} island triangles; vault at {vault.x:0.0}, {vault.z:0.0}");
            // The boat's berth: nothing solid of the island round it at the waterline (-1.1), the drawn-only buoys left out of
            // the collider (FortressIsland.NotSolid); with them a white one sat 2 m off and pinned the boat.
            string bm = "";
            float clear = float.MaxValue, buoys = float.MaxValue;
            var bv = new List<Vector3>();
            foreach (var line in File.ReadLines(Path.Combine(Src, "Models", "FortressIsland.obj")))
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "v") bv.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                else if (p[0] == "usemtl") bm = p[1];
                else if (p[0] == "f")
                    foreach (var q in p.Skip(1).Select(q => bv[int.Parse(q.Split('/')[0]) - 1]).Where(q => q.y > -2.6f && q.y < 0.9f))
                    {
                        float d = new Vector2(q.x - FortressIsland.BerthLocal.x, q.z - FortressIsland.BerthLocal.z).magnitude;
                        if (FortressIsland.NotSolid.Contains(bm)) buoys = Math.Min(buoys, d); else clear = Math.Min(clear, d);
                    }
            }
            Check(clear > 2.5f, $"something solid of the island {clear:0.0} m from the boat's berth at the waterline");
            Check(buoys < 20f, "the buoys off the pier aren't in FortressIsland.NotSolid's materials any more (renamed? the boat would catch on them)");
            // A grilled fish bought is eaten: by the buyer the game's way, or for them once they've stopped (FortressNet.EatenAnyway).
            Check(!FortressNet.LeftOver(FortressNet.FoodGrace - 0.5f, false) && FortressNet.LeftOver(FortressNet.FoodGrace, false), "a bought fish nobody eats is eaten for them after the grace, not before");
            Check(!FortressNet.LeftOver(10f, true) && FortressNet.LeftOver(20f, true), "someone eating keeps their fish (up to 20 s)");
        }

        private static (Type type, string name, string kind) Target(IEnumerable<object> attrs, (Type type, string name, string kind) t)
        {
            foreach (var a in attrs.Where(IsPatch))
            {
                object info = a.GetType().GetField("info").GetValue(a);
                var it = info.GetType();
                if (it.GetField("declaringType").GetValue(info) is Type dt) t.type = dt;
                if (it.GetField("methodName").GetValue(info) is string mn) t.name = mn;
                if (it.GetField("methodType").GetValue(info) is object mt) t.kind = mt.ToString();
            }
            return t;
        }

        // ---------- what each wave sends ----------

        public static (List<(EnemyKind kind, bool elite)> plan, int extras, float hpMult) Plan(int wave, int players = 1) =>
            FortressPlugin.PlanWave(wave, players, 3, 2, 0.4f, true); // the shipped defaults (FortressPlugin.Awake)

        private static void Waves()
        {
            Section("waves");
            float lastHp = 0f;
            for (int w = 1; w <= EnemyCatalog.FinalWave; w++)
            {
                var (plan, extras, hp) = Plan(w);
                Check(hp > lastHp, $"wave {w} health multiplier doesn't rise");
                lastHp = hp;
                Check(plan.Count > 0, $"wave {w} sends nobody");
                Check(plan.All(p => EnemyCatalog.IsBoss(p.kind) || EnemyCatalog.Of(p.kind).FirstWave <= w), $"wave {w} sends a kind before its first wave");
                var boss = w % 10 == 0 ? EnemyCatalog.BossFor(w) : (EnemyKind?)null;
                int bosses = boss != null ? 1 : 0;
                Check(plan.Count(p => EnemyCatalog.IsBoss(p.kind)) == bosses, $"wave {w} boss count");
                if (boss != null) Check(plan[0].kind == boss, $"wave {w}: the boss first");
                int land = plan.Count - bosses;
                Check(land <= FortressPlugin.LandCap, $"wave {w}: {land} fish, more than the cap");
                bool eliteWave = w % 10 != 0 && (w % 5 == 0 || (w > 10 && w % 3 == 0));
                int elites = (eliteWave ? 2 + w / 10 : 0) + (int)Math.Round(land * Math.Clamp((w - 15) / 100.0, 0.0, 1.0), MidpointRounding.AwayFromZero);
                Check(Math.Abs(plan.Count(p => p.elite) - Math.Min(elites, land)) <= 1, $"wave {w} elite count {plan.Count(p => p.elite)} (expected about {Math.Min(elites, land)})");
                Check(extras == Math.Min(12, 1 + w / 3), $"wave {w} sea harassers");
            }
            Check(Plan(10, 4).plan.Count > Plan(10, 1).plan.Count * 1.8f, "four players face nearly twice the fish");
            Check(FortressPlugin.HpMult(100) > 300f, $"wave 100 is only x{FortressPlugin.HpMult(100):0} health: OVER 99 should still be brutal, even with weapons fixed");
        }
    }
}
