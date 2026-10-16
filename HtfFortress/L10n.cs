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
            ["start"] = new[] { "Fortress mode! {0} creature types. Best so far: {1} waves. Press {2} to stop.",
                                "要塞模式開始！共 {0} 種怪物，最高紀錄 {1} 波。按 {2} 停止。",
                                "要塞模式开始！共 {0} 种怪物，最高纪录 {1} 波。按 {2} 停止。" },
            ["wave"] = new[] { "Wave {0}: {1} creatures (HP x{2})!", "第 {0} 波：{1} 隻怪物（血量 x{2}）！", "第 {0} 波：{1} 只怪物（血量 x{2}）！" },
            ["cleared"] = new[] { "Wave {0} cleared! Next wave in {1}s.", "第 {0} 波清除！{1} 秒後下一波。", "第 {0} 波清除！{1} 秒后下一波。" },
            ["stopped"] = new[] { "Stopped.", "已停止。", "已停止。" },
            ["notHost"] = new[] { "Stopped (not host anymore).", "已停止（你已不是房主）。", "已停止（你已不是房主）。" },
            ["wipe"] = new[] { "Everyone is down! Game over.", "全員倒下！遊戲結束。", "全员倒下！游戏结束。" },
            ["result"] = new[] { "{0} Cleared {1} waves.", "{0}撐過 {1} 波。", "{0}撑过 {1} 波。" },
            ["newRecord"] = new[] { " NEW RECORD!", " 新紀錄！", " 新纪录！" },
            ["best"] = new[] { " (best: {0})", "（最高：{0}）", "（最高：{0}）" },
            ["onlyHost"] = new[] { "Only the host can start waves.", "只有房主可以開始波次。", "只有房主可以开始波次。" },
            ["noCreatures"] = new[] { "No creatures to spawn, check CreaturePool.", "沒有可生成的怪物，請檢查 CreaturePool 設定。", "没有可生成的怪物，请检查 CreaturePool 设置。" },
            ["buildHud"] = new[] { "BUILD: {0}    [Q] switch   [hold LMB] place   [R] flip   [X] remove   [B] exit",
                                   "建造：{0}    [Q] 切換   [按住左鍵] 放置   [R] 反向   [X] 拆除   [B] 離開",
                                   "建造：{0}    [Q] 切换   [按住左键] 放置   [R] 反向   [X] 拆除   [B] 离开" },
            ["Wall"] = new[] { "Wall", "牆", "墙" },
            ["Floor"] = new[] { "Floor", "地板", "地板" },
            ["Ramp"] = new[] { "Ramp", "斜坡", "斜坡" },
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
