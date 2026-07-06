// ═══════════════════════════════════════════════════════════
// ── ShortcutManager ───────────────────────────────────
// ═══════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Registers keyboard shortcuts. Shift+Ctrl+F1..F12 switch scene sets.
    /// Shift+Ctrl+1..9 switch to combinations (shortcut targets).
    /// </summary>
    internal static class ShortcutManager
    {
        private const string Base = "NoBuild/";
        private const ShortcutModifiers Mods =
            ShortcutModifiers.Shift | ShortcutModifiers.Control;

        // ── Scene Sets (Shift+Ctrl+F1..F12) ─────────

        [Shortcut(Base + "Switch To Scene Set 1",  KeyCode.F1,  Mods)]
        internal static void Ss1()  => HandleSetSwitch(0);
        [Shortcut(Base + "Switch To Scene Set 2",  KeyCode.F2,  Mods)]
        internal static void Ss2()  => HandleSetSwitch(1);
        [Shortcut(Base + "Switch To Scene Set 3",  KeyCode.F3,  Mods)]
        internal static void Ss3()  => HandleSetSwitch(2);
        [Shortcut(Base + "Switch To Scene Set 4",  KeyCode.F4,  Mods)]
        internal static void Ss4()  => HandleSetSwitch(3);
        [Shortcut(Base + "Switch To Scene Set 5",  KeyCode.F5,  Mods)]
        internal static void Ss5()  => HandleSetSwitch(4);
        [Shortcut(Base + "Switch To Scene Set 6",  KeyCode.F6,  Mods)]
        internal static void Ss6()  => HandleSetSwitch(5);
        [Shortcut(Base + "Switch To Scene Set 7",  KeyCode.F7,  Mods)]
        internal static void Ss7()  => HandleSetSwitch(6);
        [Shortcut(Base + "Switch To Scene Set 8",  KeyCode.F8,  Mods)]
        internal static void Ss8()  => HandleSetSwitch(7);
        [Shortcut(Base + "Switch To Scene Set 9",  KeyCode.F9,  Mods)]
        internal static void Ss9()  => HandleSetSwitch(8);
        [Shortcut(Base + "Switch To Scene Set 10", KeyCode.F10, Mods)]
        internal static void Ss10() => HandleSetSwitch(9);
        [Shortcut(Base + "Switch To Scene Set 11", KeyCode.F11, Mods)]
        internal static void Ss11() => HandleSetSwitch(10);
        [Shortcut(Base + "Switch To Scene Set 12", KeyCode.F12, Mods)]
        internal static void Ss12() => HandleSetSwitch(11);

        // ── Combinations (Shift+Ctrl+1..9) ──────────

        [Shortcut(Base + "Switch To Combo 1", KeyCode.Alpha1, Mods)]
        internal static void Sc1() => HandleComboSwitch(0);
        [Shortcut(Base + "Switch To Combo 2", KeyCode.Alpha2, Mods)]
        internal static void Sc2() => HandleComboSwitch(1);
        [Shortcut(Base + "Switch To Combo 3", KeyCode.Alpha3, Mods)]
        internal static void Sc3() => HandleComboSwitch(2);
        [Shortcut(Base + "Switch To Combo 4", KeyCode.Alpha4, Mods)]
        internal static void Sc4() => HandleComboSwitch(3);
        [Shortcut(Base + "Switch To Combo 5", KeyCode.Alpha5, Mods)]
        internal static void Sc5() => HandleComboSwitch(4);
        [Shortcut(Base + "Switch To Combo 6", KeyCode.Alpha6, Mods)]
        internal static void Sc6() => HandleComboSwitch(5);
        [Shortcut(Base + "Switch To Combo 7", KeyCode.Alpha7, Mods)]
        internal static void Sc7() => HandleComboSwitch(6);
        [Shortcut(Base + "Switch To Combo 8", KeyCode.Alpha8, Mods)]
        internal static void Sc8() => HandleComboSwitch(7);
        [Shortcut(Base + "Switch To Combo 9", KeyCode.Alpha9, Mods)]
        internal static void Sc9() => HandleComboSwitch(8);

        // ── Handlers ────────────────────────────────

        private static void HandleSetSwitch(int index)
        {
            NoBuildSettings s = NoBuildResourceUtility.GetSettings();
            if (s == null || !s.shortcutsEnabled) return;
            if (index < 0 || index >= s.sceneSets.Count) return;

            s.activeSceneSetIndex = index;
            EditorUtility.SetDirty(s);
            SceneSwitcher.SwitchToSet(s.sceneSets[index]);
        }

        private static void HandleComboSwitch(int index)
        {
            NoBuildSettings s = NoBuildResourceUtility.GetSettings();
            if (s == null || !s.shortcutsEnabled) return;

            SceneSet active = s.ActiveSceneSet;
            if (active == null) return;
            List<SceneCombination> combos = active.GetEnabledCombinations();
            if (index < 0 || index >= combos.Count) return;

            SceneSwitcher.SwitchToCombination(combos[index]);
        }
    }
}
