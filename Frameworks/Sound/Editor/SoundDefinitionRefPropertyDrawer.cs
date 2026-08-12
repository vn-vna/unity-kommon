using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Sound.Editor
{
    [CustomPropertyDrawer(typeof(SoundDefinitionRef))]
    internal sealed class SoundDefinitionRefPropertyDrawer : PropertyDrawer
    {
        #region Constants

        private const int BadgeWidth = 34;
        private const int BadgeHeight = 16;
        private const int ButtonWidth = 22;
        private const int RowGap = 2;

        private static readonly Color BadgeSfx = new Color(0.35f, 0.45f, 0.75f);
        private static readonly Color BadgeBgm = new Color(0.65f, 0.35f, 0.8f);

        #endregion

        #region PropertyDrawer

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty defProp = property.FindPropertyRelative("_definition");
            bool hasDefinition = defProp != null && defProp.objectReferenceValue != null;
            float lineHeight = EditorGUIUtility.singleLineHeight;

            return hasDefinition
                ? lineHeight * 2f + RowGap
                : lineHeight;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty defProp = property.FindPropertyRelative("_definition");
            if (defProp == null) return;

            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect row1 = new Rect(
                position.x, position.y,
                position.width, lineHeight);

            EditorGUI.PropertyField(row1, defProp, label);

            var def = defProp.objectReferenceValue as SoundDefinition;
            if (def == null) return;

            Rect row2 = new Rect(
                position.x + 16f,
                position.y + lineHeight + RowGap,
                position.width - 16f - ButtonWidth * 2f - BadgeWidth - 6f,
                lineHeight);

            DrawBusBadge(
                new Rect(row2.x, row2.y + (lineHeight - BadgeHeight) * 0.5f,
                    BadgeWidth, BadgeHeight),
                def);

            row2.x += BadgeWidth + 6f;
            row2.width -= BadgeWidth + 6f;
            EditorGUI.LabelField(row2, BuildSummary(def));

            float buttonX = position.xMax - ButtonWidth * 2f - 2f;
            Rect playRect = new Rect(
                buttonX, row2.y + (lineHeight - BadgeHeight) * 0.5f,
                ButtonWidth, BadgeHeight);
            Rect stopRect = new Rect(
                buttonX + ButtonWidth + 2f,
                row2.y + (lineHeight - BadgeHeight) * 0.5f,
                ButtonWidth, BadgeHeight);

            if (GUI.Button(playRect, "\u25B6"))
            {
                Preview(def);
            }
            if (GUI.Button(stopRect, "\u25A0"))
            {
                StopPreview();
            }
        }

        #endregion

        #region Private Methods

        private static void DrawBusBadge(Rect rect, SoundDefinition def)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = def.IsSfx ? BadgeSfx : BadgeBgm;
            GUI.Box(rect, def.IsSfx ? "SFX" : "BGM", EditorStyles.miniButton);
            GUI.backgroundColor = oldBg;
        }

        private static string BuildSummary(SoundDefinition def)
        {
            if (def.Clip == null)
            {
                return def.DisplayLabel + " (no clip)";
            }

            return def.IsBgm
                ? def.DisplayLabel + " | loop=" + def.Loop
                    + " fade=" + def.FadeInSeconds + "/" + def.FadeOutSeconds + "s"
                : def.DisplayLabel + " | " + Mathf.RoundToInt(def.Volume * 100f)
                    + "% pitch=" + def.Pitch + " loop=" + def.Loop;
        }

        private static void Preview(SoundDefinition def)
        {
            if (def == null || def.Clip == null) return;

            if (EditorApplication.isPlaying)
            {
                if (def.IsBgm)
                {
                    SoundManager.Instance?.PlayBgm(def);
                }
                else
                {
                    SoundManager.Instance?.PlaySfx(def);
                }
                return;
            }

            StopPreview();
            EnsurePreviewSource();

            _previewSource.clip = def.Clip;
            _previewSource.volume = def.Volume;
            _previewSource.pitch = def.Pitch;
            _previewSource.loop = def.Loop;
            _previewSource.Play();
        }

        private static void StopPreview()
        {
            if (_previewSource != null)
            {
                _previewSource.Stop();
                _previewSource.clip = null;
            }
        }

        private static void EnsurePreviewSource()
        {
            if (_previewSource != null) return;

            GameObject go = new GameObject("[Sound Preview]");
            go.hideFlags = HideFlags.HideAndDontSave;
            _previewSource = go.AddComponent<AudioSource>();
            _previewSource.playOnAwake = false;
        }

        private static AudioSource _previewSource;

        #endregion
    }
}
