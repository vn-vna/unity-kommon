// ═══════════════════════════════════════════════════════════
// ── EditorGuiColors ───────────────────────────────────
// ═══════════════════════════════════════════════════════════

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Shared color constants for editor GUI elements.
    /// </summary>
    public static class EditorGuiColors
    {
        #region Action Buttons

        public static readonly Color BuildGreen = new Color(0.3f, 0.7f, 0.3f);
        public static readonly Color RunBlue = new Color(0.3f, 0.5f, 0.9f);

        #endregion

        #region State Indicators

        public static readonly Color ActiveGreen = Color.green;
        public static readonly Color InactiveGray = Color.gray;
        public static readonly Color WarningAmber = new Color(0.9f, 0.7f, 0.3f);

        #endregion

        #region Selection & Hover

        public static readonly Color HoverBlue = new Color(0.25f, 0.5f, 0.85f, 0.5f);
        public static readonly Color SelectionBlueBg = new Color(0.2f, 0.4f, 0.7f, 0.45f);
        public static readonly Color HoverHoverBg = new Color(0.35f, 0.5f, 0.75f, 0.3f);

        #endregion

        #region Status

        public static readonly Color InstalledGreen = new Color(0.2f, 0.6f, 0.2f);
        public static readonly Color QueuedBlue = new Color(0.2f, 0.4f, 0.8f);
        public static readonly Color RemoveRed = new Color(0.8f, 0.2f, 0.2f);

        #endregion
    }
}
