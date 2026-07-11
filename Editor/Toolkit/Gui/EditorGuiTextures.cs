// ═══════════════════════════════════════════════════════════
// ── EditorGuiTextures ─────────────────────────────────
// ═══════════════════════════════════════════════════════════

using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Editor.Toolkit
{
    /// <summary>
    /// Shared texture factory for IMGUI style backgrounds, hover highlights, and other procedural textures.
    /// </summary>
    public static class EditorGuiTextures
    {
        #region Cached Textures

        private static Texture2D _clearTex;
        private static Texture2D _whiteTex;

        public static Texture2D ClearTex
        {
            get
            {
                if (_clearTex == null)
                {
                    _clearTex = MakeTex(1, 1, Color.clear);
                    _clearTex.hideFlags = HideFlags.HideAndDontSave;
                }
                return _clearTex;
            }
        }

        public static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = MakeTex(1, 1, Color.white);
                    _whiteTex.hideFlags = HideFlags.HideAndDontSave;
                }
                return _whiteTex;
            }
        }

        #endregion

        #region Public Methods

        public static Texture2D MakeTex(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        #endregion
    }
}
