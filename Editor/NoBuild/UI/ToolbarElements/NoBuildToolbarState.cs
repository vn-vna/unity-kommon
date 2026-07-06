// ═══════════════════════════════════════════════════════════
// ── NoBuildToolbarState ───────────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Shared state container for all NoBuild toolbar elements.
    /// Provides the <see cref="NoBuildSettings"/> reference and a repaint
    /// mechanism so that toolbar elements stay synchronized.
    /// </summary>
    internal static class NoBuildToolbarState
    {
        /// <summary>Invoked when any toolbar element should repaint.</summary>
        public static event Action RepaintRequested;

        /// <summary>Triggers a repaint on all registered toolbar elements.</summary>
        public static void RequestRepaint()
        {
            RepaintRequested?.Invoke();
        }

        /// <summary>Returns the current NoBuildSettings (never null in normal operation).</summary>
        public static NoBuildSettings GetSettings()
        {
            return NoBuildResourceUtility.GetOrCreateSettings();
        }
    }
}
