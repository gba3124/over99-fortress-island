using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine.Localization.Settings;

namespace HtfFortress
{
    public enum LanguageSetting { Auto, English, TraditionalChinese, SimplifiedChinese }

    // Tiny string table. Auto follows the game's own language setting.
    public static class L10n
    {
        public static ConfigEntry<LanguageSetting> Setting;

        private enum Lang { En, ZhTW, ZhCN }

        // key -> { English, 繁體中文, 简体中文 }
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            ["prefix"] = new[] { "[Fortress]", "[要塞]", "[要塞]" },
            ["start"] = new[] { "The siege begins! Best so far: {0} waves.", "要塞防禦開始！最高紀錄 {0} 波。", "要塞防御开始！最高纪录 {0} 波。" },
            ["wave"] = new[] { "Wave {0}: {1} creatures (HP x{2})!", "第 {0} 波：{1} 隻怪物（血量 x{2}）！", "第 {0} 波：{1} 只怪物（血量 x{2}）！" },
            ["cleared"] = new[] { "Wave {0} cleared! +{1} coins. Build time {2}s (host: {3} to start now).",
                                  "第 {0} 波清除！+{1} 金幣。建造時間 {2} 秒（房主按 {3} 立即開始）。",
                                  "第 {0} 波清除！+{1} 金币。建造时间 {2} 秒（房主按 {3} 立即开始）。" },
            ["startHint"] = new[] { "Press {0} to build walls and turrets. Host: {1} starts the next wave early.",
                                    "按 {0} 建造牆和砲塔。房主按 {1} 可以提早開始下一波。",
                                    "按 {0} 建造墙和炮塔。房主按 {1} 可以提早开始下一波。" },
            ["stopped"] = new[] { "Stopped.", "已停止。", "已停止。" },
            ["notHost"] = new[] { "Stopped (not host anymore).", "已停止（你已不是房主）。", "已停止（你已不是房主）。" },
            ["wipe"] = new[] { "Everyone is down! Game over.", "全員倒下！遊戲結束。", "全员倒下！游戏结束。" },
            ["result"] = new[] { "{0} Cleared {1} waves.", "{0}撐過 {1} 波。", "{0}撑过 {1} 波。" },
            ["newRecord"] = new[] { " NEW RECORD!", " 新紀錄！", " 新纪录！" },
            ["best"] = new[] { " (best: {0})", "（最高：{0}）", "（最高：{0}）" },
            ["onlyHost"] = new[] { "Only the host can start waves.", "只有房主可以開始波次。", "只有房主可以开始波次。" },
            ["noCreatures"] = new[] { "No creatures to spawn, check CreaturePool.", "沒有可生成的怪物，請檢查 CreaturePool 設定。", "没有可生成的怪物，请检查 CreaturePool 设置。" },
            ["buildHud"] = new[] { "BUILD: {0}    [hold LMB] place   [Y] flip   [U] upgrade   [J] repair   [H] remove (half back)   [G] exit",
                                   "建造：{0}    [按住左鍵] 放置   [Y] 反向   [U] 升級   [J] 修理   [H] 拆除（退一半）   [G] 離開",
                                   "建造：{0}    [按住左键] 放置   [Y] 反向   [U] 升级   [J] 修理   [H] 拆除（退一半）   [G] 离开" },
            ["cost"] = new[] { " ({0} coins)", "（{0} 金幣）", "（{0} 金币）" },
            ["free"] = new[] { " (free outside fortress mode)", "（要塞模式外免費）", "（要塞模式外免费）" },
            ["lookInfo"] = new[] { "{0}  Lv{1}  HP {2}/{3}  {4}", "{0}  Lv{1}  耐久 {2}/{3}  {4}", "{0}  Lv{1}  耐久 {2}/{3}  {4}" },
            ["upgradeHint"] = new[] { "[U] upgrade: {0} coins", "[U] 升級：{0} 金幣", "[U] 升级：{0} 金币" },
            ["maxLevel"] = new[] { "(max level)", "（已滿級）", "（已满级）" },
            ["buildPhase"] = new[] { "Build time: {0}s", "建造時間：{0} 秒", "建造时间：{0} 秒" },
            ["fightPhase"] = new[] { "Defend the core!", "守住核心！", "守住核心！" },
            ["siegeHud"] = new[] { "Wave {0}     Core {1}/{2}     Enemies {3}     Coins {4}", "第 {0} 波     核心 {1}/{2}     敵人 {3}     金幣 {4}", "第 {0} 波     核心 {1}/{2}     敌人 {3}     金币 {4}" },
            ["coreDown"] = new[] { "The core was destroyed! Game over.", "核心被摧毀！遊戲結束。", "核心被摧毁！游戏结束。" },
            ["noLand"] = new[] { "No land near the core, so walkers can't spawn. Start on an island to get walkers.", "核心附近沒有陸地，登陸魚無法出現。請站在島上再開始。", "核心附近没有陆地，登陆鱼无法出现。请站在岛上再开始。" },
            ["islandUnlocked"] = new[] { "A new island has appeared on your radar...", "雷達上出現了一座新的島……", "雷达上出现了一座新的岛……" },
            ["islandTitle"] = new[] { "<size=120%>Fortress Island</size>", "<size=120%>要塞島</size>", "<size=120%>要塞岛</size>" },
            ["readyPrompt"] = new[] { "Everyone on the island: stand in the glowing circle by the lighthouse to begin ({0}/{1})",
                                      "島上所有人站進燈塔前的光圈就會開始（{0}/{1}）",
                                      "岛上所有人站进灯塔前的光圈就会开始（{0}/{1}）" },
            ["readyCountdown"] = new[] { "Starting in {0}...", "{0} 秒後開始……", "{0} 秒后开始……" },
            ["resultCore"] = new[] { "<size=130%>The lighthouse has fallen</size>", "<size=130%>燈塔陷落了</size>", "<size=130%>灯塔陷落了</size>" },
            ["resultWipe"] = new[] { "<size=130%>Everyone is down</size>", "<size=130%>全員倒下</size>", "<size=130%>全员倒下</size>" },
            ["resultStats"] = new[] { "Waves cleared {0}     Kills {1}     Built {2}", "撐過 {0} 波     擊殺 {1}     建造 {2}", "撑过 {0} 波     击杀 {1}     建造 {2}" },
            ["resultRecord"] = new[] { "<color=#ffd040>NEW RECORD!</color>", "<color=#ffd040>新紀錄！</color>", "<color=#ffd040>新纪录！</color>" },
            ["resultBest"] = new[] { "Best: {0} waves", "最高紀錄：{0} 波", "最高纪录：{0} 波" },
            ["resultAgain"] = new[] { "Stand in the circle to try again", "站進光圈再挑戰一次", "站进光圈再挑战一次" },
            ["needRound"] = new[] { "Start a round first (stand in the circle)", "先站進光圈開始一局", "先站进光圈开始一局" },
            ["needIsland"] = new[] { "You can only build on the fortress island", "只能在要塞島上建造", "只能在要塞岛上建造" },
            ["needCoins"] = new[] { "Not enough coins", "金幣不足", "金币不足" },
            ["waveBanner"] = new[] { "Wave {0}", "第 {0} 波", "第 {0} 波" },
            ["eliteBanner"] = new[] { "Wave {0}: elites incoming!", "第 {0} 波：精英來襲！", "第 {0} 波：精英来袭！" },
            ["bossBanner"] = new[] { "Wave {0}: THE BEHEMOTH RISES", "第 {0} 波：深海巨獸登陸！", "第 {0} 波：深海巨兽登陆！" },
            ["clearedBanner"] = new[] { "Wave {0} cleared", "第 {0} 波清除", "第 {0} 波清除" },
            ["bossHud"] = new[] { "Behemoth {0}/{1}", "深海巨獸 {0}/{1}", "深海巨兽 {0}/{1}" },
            ["Harpoon"] = new[] { "Harpoon", "魚叉塔", "鱼叉塔" },
            ["Mortar"] = new[] { "Mortar", "迫擊砲", "迫击炮" },
            ["Tesla"] = new[] { "Tesla coil", "電網塔", "电网塔" },
            ["Spikes"] = new[] { "Spike trap", "尖刺陷阱", "尖刺陷阱" },
            ["GunA"] = new[] { "Storm", "暴風", "暴风" }, ["GunB"] = new[] { "Armour-piercing", "穿甲", "穿甲" },
            ["CannonA"] = new[] { "Cluster", "集束", "集束" }, ["CannonB"] = new[] { "Heavy", "重砲", "重炮" },
            ["HarpoonA"] = new[] { "Multi-shot", "多重", "多重" }, ["HarpoonB"] = new[] { "Freeze", "冰凍", "冰冻" },
            ["MortarA"] = new[] { "Firestorm", "燃燒", "燃烧" }, ["MortarB"] = new[] { "Barrage", "連發", "连发" },
            ["TeslaA"] = new[] { "Spread", "擴散", "扩散" }, ["TeslaB"] = new[] { "Overload", "超載", "超载" },
            ["branchHint"] = new[] { "[U] {0} / [Shift+U] {1}: {2} coins", "[U] {0} / [Shift+U] {1}：{2} 金幣", "[U] {0} / [Shift+U] {1}：{2} 金币" },
            ["repairHint"] = new[] { "[J] repair: {0} coins", "[J] 修理：{0} 金幣", "[J] 修理：{0} 金币" },
            ["barHint"] = new[] { "[T] next  [Shift+T] back", "[T] 下一個  [Shift+T] 上一個", "[T] 下一个  [Shift+T] 上一个" },
            ["islandWelcome"] = new[] { "<size=120%>Fortress Island</size>\nHold the lighthouse", "<size=120%>要塞島</size>\n守住燈塔", "<size=120%>要塞岛</size>\n守住灯塔" },
            ["netHud"] = new[] { "Net {0}/{1}  (worth {2})", "漁網 {0}/{1}（價值 {2}）", "渔网 {0}/{1}（价值 {2}）" },
            ["netFull"] = new[] { "FULL - sell at the fish crate by the dock", "滿了，到碼頭旁的魚箱賣掉", "满了，到码头旁的鱼箱卖掉" },
            ["netUpgradeHint"] = new[] { "[U] bigger net ({0} fish): {1} coins", "[U] 升級漁網（裝 {0} 隻）：{1} 金幣", "[U] 升级渔网（装 {0} 只）：{1} 金币" },
            ["netMax"] = new[] { "Net at max size", "漁網已是最大", "渔网已是最大" },
            ["netUpgraded"] = new[] { "Bigger nets! Everyone now carries {0} fish", "漁網升級！每人可裝 {0} 隻", "渔网升级！每人可装 {0} 只" },
            ["tableHeader"] = new[] { "Player   Kills   Turret kills   Fished   Built   Downs", "玩家   擊殺   砲塔擊殺   捕魚收入   建造   倒下", "玩家   击杀   炮塔击杀   捕鱼收入   建造   倒下" },
            ["mvpKills"] = new[] { "Kill King", "擊殺王", "击杀王" },
            ["mvpBuilder"] = new[] { "Master Builder", "建築大師", "建筑大师" },
            ["mvpFisher"] = new[] { "Fisher King", "漁夫王", "渔夫王" },
            ["mvpSurvivor"] = new[] { "Never Went Down", "不倒翁", "不倒翁" },
            ["fishTip"] = new[] { "Dead fish are the money: scoop them with your net and sell at the crate by the dock. Watch out, they bite now!",
                                  "魚屍才是主要收入：用漁網撿起來，拿到碼頭旁的魚箱賣。小心，牠們現在會咬人了！",
                                  "鱼尸才是主要收入：用渔网捡起来，拿到码头旁的鱼箱卖。小心，它们现在会咬人了！" },
            ["Wall"] = new[] { "Wall", "牆", "墙" },
            ["Floor"] = new[] { "Floor", "地板", "地板" },
            ["Ramp"] = new[] { "Ramp", "斜坡", "斜坡" },
            ["WallStone"] = new[] { "Stone wall", "石牆", "石墙" },
            ["WallMetal"] = new[] { "Metal wall", "鐵牆", "铁墙" },
            ["Cannon"] = new[] { "Cannon", "大砲", "大炮" },
            ["Gun"] = new[] { "Machine gun", "機槍塔", "机枪塔" },
        };

        public static string T(string key, params object[] args)
        {
            if (!Table.TryGetValue(key, out var row)) return key;
            string s = row[(int)Current()];
            return args.Length == 0 ? s : string.Format(s, args);
        }

        private static Lang Current()
        {
            switch (Setting?.Value ?? LanguageSetting.Auto)
            {
                case LanguageSetting.English: return Lang.En;
                case LanguageSetting.TraditionalChinese: return Lang.ZhTW;
                case LanguageSetting.SimplifiedChinese: return Lang.ZhCN;
            }
            string code;
            try { code = LocalizationSettings.SelectedLocale?.Identifier.Code ?? ""; }
            catch { code = ""; }
            if (!code.StartsWith("zh")) return Lang.En;
            return code.Contains("TW") || code.Contains("HK") || code.Contains("Hant") ? Lang.ZhTW : Lang.ZhCN;
        }
    }
}
