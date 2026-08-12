using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics.Editor
{
    /// <summary>
    /// Compact inline row for a <see cref="HapticKeyframe"/>:
    /// time / duration / intensity slider / waveform popup.
    /// Uses GUILayout so widths can never go negative (IMGUI throws on
    /// negative rect widths, which locks the editor).
    /// </summary>
    [CustomPropertyDrawer(typeof(HapticKeyframe))]
    internal sealed class HapticRhythmKeyframeDrawer : PropertyDrawer
    {
        #region Constants

        private const float TimeFieldWidth = 52f;
        private const float DurationFieldWidth = 52f;
        private const float IntensitySliderWidth = 90f;
        private const float WaveformMinWidth = 90f;

        #endregion

        #region PropertyDrawer

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty timeProp = property.FindPropertyRelative("_timeSeconds");
            SerializedProperty durationProp = property.FindPropertyRelative("_durationSeconds");
            SerializedProperty intensityProp = property.FindPropertyRelative("_intensity");
            SerializedProperty waveformProp = property.FindPropertyRelative("_waveform");

            if (timeProp == null || durationProp == null || intensityProp == null || waveformProp == null)
            {
                EditorGUI.LabelField(position, "HapticKeyframe layout changed.");
                return;
            }

            Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            using (new EditorGUI.ChangeCheckScope())
            {
                float x = row.x;
                const float gap = 4f;

                // Time
                Rect timeLabel = new Rect(x, row.y, 12f, row.height);
                EditorGUI.LabelField(timeLabel, "t");
                x += timeLabel.width + 2f;
                Rect timeField = new Rect(x, row.y, TimeFieldWidth, row.height);
                timeProp.floatValue = Mathf.Max(0f, EditorGUI.FloatField(timeField, timeProp.floatValue));
                x += TimeFieldWidth + gap;

                // Duration
                Rect durLabel = new Rect(x, row.y, 24f, row.height);
                EditorGUI.LabelField(durLabel, "dur");
                x += durLabel.width + 2f;
                Rect durField = new Rect(x, row.y, DurationFieldWidth, row.height);
                durationProp.floatValue = Mathf.Max(0.01f, EditorGUI.FloatField(durField, durationProp.floatValue));
                x += DurationFieldWidth + gap;

                // Intensity
                Rect intLabel = new Rect(x, row.y, 22f, row.height);
                EditorGUI.LabelField(intLabel, "int");
                x += intLabel.width + 2f;
                Rect intSlider = new Rect(x, row.y, IntensitySliderWidth, row.height);
                intensityProp.floatValue = EditorGUI.Slider(intSlider, intensityProp.floatValue, 0f, 1f);
                x += IntensitySliderWidth + gap;

                // Waveform — take whatever width remains, never negative.
                float remaining = row.x + row.width - x;
                float waveformWidth = Mathf.Max(WaveformMinWidth, remaining);
                Rect waveformField = new Rect(x, row.y, waveformWidth, row.height);
                waveformProp.enumValueIndex = EditorGUI.Popup(
                    waveformField, waveformProp.enumValueIndex,
                    System.Enum.GetNames(typeof(HapticWaveformType)));

                if (GUI.changed)
                {
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        #endregion
    }
}
