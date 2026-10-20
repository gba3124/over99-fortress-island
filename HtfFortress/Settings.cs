using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HtfFortress
{
    // Each player's own display settings (never the host's): on the [I] panel's last page (Tips), so friends without Mod
    // Menu can change them in the game, and in the BepInEx config. The playtest's late waves lagged (Owen's friends:
    // 「我覺得需要把平常打的傷害那些的關掉 / 你說特效太多」). Another on/off is one Add(...) before the panel is first opened.
    public static class ClientSettings
    {
        public static ConfigEntry<bool> ReducedEffects;
        public static bool Reduced => ReducedEffects != null && ReducedEffects.Value;

        // One line on the settings page: its name (L10n key, with "<key>D" under it), whether it's on, what turning it does.
        public struct Row { public string Key; public Func<bool> On; public Action<bool> Set; }
        public static readonly List<Row> Rows = new List<Row>();
        public static void Add(string key, Func<bool> on, Action<bool> set) => Rows.Add(new Row { Key = key, On = on, Set = set });
        public static void Add(string key, ConfigEntry<bool> entry) => Add(key, () => entry.Value, v => entry.Value = v);

        public static void Bind(ConfigFile config)
        {
            ReducedEffects = config.Bind("Display", "ReducedEffects", false,
                "This screen only: fewer particles, shorter gas clouds and fewer leftover effects (for lag in the late waves).");
            // The game's own option (its Options menu has it too): the same setting, so the two always agree.
            Add("setDamageNumbers", () => DecalManager.UseDamageNumbers, DamageNumbers);
            Add("setReducedFx", ReducedEffects);
        }

        private static void DamageNumbers(bool on)
        {
            PlayerPrefs.SetInt("DamageNumbers", on ? 1 : 0);
            DecalManager.ToggleDamageNumbers(on);
            try
            {
                var menu = AccessTools.Field(typeof(ButtonManager), "_instance")?.GetValue(null);
                if (menu != null && AccessTools.Field(typeof(ButtonManager), "_damageNumbersToggle")?.GetValue(menu) is Toggle t && t) t.SetIsOnWithoutNotify(on);
            }
            catch (Exception) { } // the options screen just shows it next time it's built
        }
    }
}
