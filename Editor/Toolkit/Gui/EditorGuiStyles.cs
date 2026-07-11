// ═══════════════════════════════════════════════════════════
// ── EditorGuiStyles ───────────────────────────────────
// ═══════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Centralized GUIStyle registry for consistent look across all editor tools.
    /// All styles are lazily initialized on first access — no explicit Init() required.
    /// </summary>
    public static class EditorGuiStyles
    {
        #region Section Styles

        private static GUIStyle _headerTitle;

        public static GUIStyle HeaderTitle
        {
            get
            {
                if (_headerTitle == null)
                {
                    _headerTitle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 14,
                        padding = new RectOffset(6, 6, 4, 2),
                        normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
                    };
                }
                return _headerTitle;
            }
        }

        private static GUIStyle _headerSubtitle;

        public static GUIStyle HeaderSubtitle
        {
            get
            {
                if (_headerSubtitle == null)
                {
                    _headerSubtitle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        padding = new RectOffset(8, 8, 0, 4),
                        normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }
                    };
                }
                return _headerSubtitle;
            }
        }

        private static GUIStyle _sectionHeader;

        public static GUIStyle SectionHeader
        {
            get
            {
                if (_sectionHeader == null)
                {
                    _sectionHeader = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        padding = new RectOffset(0, 0, 4, 4),
                        normal = { textColor = new Color(0.8f, 0.85f, 0.9f) }
                    };
                }
                return _sectionHeader;
            }
        }

        #endregion

        #region Card & Container Styles

        private static GUIStyle _card;

        public static GUIStyle Card
        {
            get
            {
                if (_card == null)
                {
                    _card = new GUIStyle(EditorStyles.helpBox)
                    {
                        padding = new RectOffset(10, 10, 8, 8),
                        margin = new RectOffset(4, 4, 2, 2)
                    };
                }
                return _card;
            }
        }

        #endregion

        #region Badge & Status Styles

        private static GUIStyle _badge;

        public static GUIStyle Badge
        {
            get
            {
                if (_badge == null)
                {
                    _badge = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(6, 6, 2, 2),
                        fontSize = 10
                    };
                }
                return _badge;
            }
        }

        private static GUIStyle _inlineStatus;

        public static GUIStyle InlineStatus
        {
            get
            {
                if (_inlineStatus == null)
                {
                    _inlineStatus = new GUIStyle(EditorStyles.miniLabel)
                    {
                        richText = true,
                        padding = new RectOffset(4, 4, 2, 2)
                    };
                }
                return _inlineStatus;
            }
        }

        private static GUIStyle _centeredLabel;

        public static GUIStyle CenteredLabel
        {
            get
            {
                if (_centeredLabel == null)
                {
                    _centeredLabel = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleCenter
                    };
                }
                return _centeredLabel;
            }
        }

        #endregion

        #region Button Styles

        private static GUIStyle _modeButtonActive;

        public static GUIStyle ModeButtonActive
        {
            get
            {
                if (_modeButtonActive == null)
                {
                    _modeButtonActive = new GUIStyle(EditorStyles.miniButton)
                    {
                        normal = { textColor = Color.white },
                        fontStyle = FontStyle.Bold
                    };
                }
                return _modeButtonActive;
            }
        }

        private static GUIStyle _modeButtonInactive;

        public static GUIStyle ModeButtonInactive
        {
            get
            {
                if (_modeButtonInactive == null)
                {
                    _modeButtonInactive = new GUIStyle(EditorStyles.miniButton)
                    {
                        normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }
                    };
                }
                return _modeButtonInactive;
            }
        }

        #endregion

        #region Text Entry Styles

        private static GUIStyle _commandArea;

        public static GUIStyle CommandArea
        {
            get
            {
                if (_commandArea == null)
                {
                    _commandArea = new GUIStyle(EditorStyles.textArea)
                    {
                        font = EditorStyles.standardFont,
                        fontSize = 12,
                        padding = new RectOffset(8, 8, 6, 6),
                        wordWrap = false
                    };
                }
                return _commandArea;
            }
        }

        private static GUIStyle _historyEntry;

        public static GUIStyle HistoryEntry
        {
            get
            {
                if (_historyEntry == null)
                {
                    _historyEntry = new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 11,
                        wordWrap = true,
                        padding = new RectOffset(4, 4, 2, 2),
                        normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }
                    };
                }
                return _historyEntry;
            }
        }

        #endregion

        #region Hover Styles

        private static GUIStyle _hoverLabel;

        public static GUIStyle HoverLabel
        {
            get
            {
                if (_hoverLabel == null)
                    _hoverLabel = CreateHoverStyle(EditorStyles.label);
                return _hoverLabel;
            }
        }

        private static GUIStyle _hoverBoldLabel;

        public static GUIStyle HoverBoldLabel
        {
            get
            {
                if (_hoverBoldLabel == null)
                    _hoverBoldLabel = CreateHoverStyle(EditorStyles.boldLabel);
                return _hoverBoldLabel;
            }
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Creates a GUIStyle based on <paramref name="baseStyle"/> with a blue hover highlight.
        /// </summary>
        public static GUIStyle CreateHoverStyle(GUIStyle baseStyle)
        {
            GUIStyle style = new GUIStyle(baseStyle)
            {
                padding = new RectOffset(4, 4, 3, 3),
                hover = new GUIStyleState
                {
                    textColor = Color.white,
                    background = EditorGuiTextures.MakeTex(1, 1, EditorGuiColors.HoverBlue)
                }
            };
            return style;
        }

        /// <summary>
        /// Creates a sidebar list entry style suitable for selection lists.
        /// </summary>
        public static GUIStyle CreateSidebarEntryStyle(
            float fixedHeight,
            Color normalColor,
            Color? hoverColor = null)
        {
            Texture2D clearBg = EditorGuiTextures.ClearTex;

            return new GUIStyle
            {
                fixedHeight = fixedHeight,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 4, 0, 0),
                margin = new RectOffset(),
                clipping = TextClipping.Clip,
                fontSize = 11,
                normal = { textColor = normalColor, background = clearBg },
                hover = { textColor = hoverColor ?? Color.white, background = clearBg }
            };
        }

        #endregion
    }
}
