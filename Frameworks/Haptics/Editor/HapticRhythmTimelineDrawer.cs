using System;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Haptics.Editor
{
    /// <summary>
    /// Static timeline drawing helper for the rhythm editor.
    /// Draws a time ruler, keyframe blocks, drag + snap (0.05s),
    /// right-click add, and click-to-select. Mutates the rhythm SO
    /// through the provided SerializedObject.
    /// </summary>
    internal static class HapticRhythmTimelineDrawer
    {
        #region Constants

        private const float RulerHeight = 18f;
        private const float TrackPadding = 2f;
        private const float BlockMinWidth = 8f;
        private const float SnapStep = 0.05f;
        private const float TickStep = 0.25f;

        private static readonly Color LightImpactColor = new Color(0.35f, 0.5f, 0.85f);
        private static readonly Color MediumImpactColor = new Color(0.35f, 0.7f, 0.45f);
        private static readonly Color HeavyImpactColor = new Color(0.9f, 0.4f, 0.4f);
        private static readonly Color NotificationColor = new Color(0.65f, 0.4f, 0.8f);
        private static readonly Color SelectionTickColor = new Color(0.3f, 0.75f, 0.8f);
        private static readonly Color ContinuousColor = new Color(0.55f, 0.55f, 0.55f);

        private static readonly Color RulerBg = new Color(0.15f, 0.15f, 0.15f);
        private static readonly Color GridColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        private static readonly Color SelectionColor = Color.white;
        private static readonly Color TrackBg = new Color(0.1f, 0.1f, 0.1f);

        private static int _dragIndex = -1;
        private static float _dragOffsetSeconds;

        #endregion

        #region Public Methods

        /// <summary>
        /// Draws the full timeline inside <paramref name="rect"/>. Mutates the
        /// keyframes array via <paramref name="rhythmSo"/> and sets
        /// <paramref name="selectedIndex"/> when a block is clicked.
        /// </summary>
        public static void DrawTimeline(
            Rect rect,
            HapticRhythm rhythm,
            SerializedObject rhythmSo,
            ref int selectedIndex)
        {
            if (rhythm == null || rhythmSo == null) return;

            SerializedProperty keyframesProp = rhythmSo.FindProperty("_keyframes");
            if (keyframesProp == null || !keyframesProp.isArray) return;

            float duration = Mathf.Max(rhythm.ComputeDuration(), 0.01f);

            Rect rulerRect = new Rect(rect.x, rect.y, rect.width, RulerHeight);
            Rect trackRect = new Rect(
                rect.x,
                rect.y + RulerHeight + TrackPadding,
                rect.width,
                rect.height - RulerHeight - TrackPadding);

            HandleTimelineEvents(trackRect, keyframesProp, duration, rhythmSo, ref selectedIndex);

            DrawRuler(rulerRect, duration);
            DrawTrack(trackRect, keyframesProp, duration, selectedIndex);

            rhythmSo.ApplyModifiedProperties();
        }

        /// <summary>
        /// Adds a keyframe to the serialized array at the given time (snapped).
        /// </summary>
        public static int AddKeyframe(
            SerializedProperty keyframesProp,
            float timeSeconds,
            HapticWaveformType waveform = HapticWaveformType.LightImpact)
        {
            if (keyframesProp == null || !keyframesProp.isArray) return -1;

            int index = keyframesProp.arraySize;
            keyframesProp.InsertArrayElementAtIndex(index);
            SerializedProperty element = keyframesProp.GetArrayElementAtIndex(index);

            SerializedProperty timeProp = element.FindPropertyRelative("_timeSeconds");
            SerializedProperty durationProp = element.FindPropertyRelative("_durationSeconds");
            SerializedProperty intensityProp = element.FindPropertyRelative("_intensity");
            SerializedProperty waveformProp = element.FindPropertyRelative("_waveform");

            timeProp.floatValue = Snap(timeSeconds);
            durationProp.floatValue = 0.05f;
            intensityProp.floatValue = 0.5f;
            waveformProp.enumValueIndex = (int)waveform;

            keyframesProp.serializedObject.ApplyModifiedProperties();
            return index;
        }

        #endregion

        #region Private Methods

        private static void DrawRuler(Rect rect, float duration)
        {
            EditorGUI.DrawRect(rect, RulerBg);

            for (float t = 0f; t <= duration + 0.001f; t += TickStep)
            {
                float x = rect.x + (t / duration) * rect.width;
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, RulerHeight), GridColor);

                GUI.Label(
                    new Rect(x + 2f, rect.y, 42f, RulerHeight),
                    t.ToString("0.00"),
                    EditorStyles.miniLabel);
            }

            GUI.Label(
                new Rect(rect.x + rect.width - 44f, rect.y, 42f, RulerHeight),
                duration.ToString("0.00") + "s",
                EditorStyles.miniLabel);
        }

        private static void DrawTrack(
            Rect rect,
            SerializedProperty keyframesProp,
            float duration,
            int selectedIndex)
        {
            EditorGUI.DrawRect(rect, TrackBg);

            for (int i = 0; i < keyframesProp.arraySize; i++)
            {
                SerializedProperty element = keyframesProp.GetArrayElementAtIndex(i);

                float time = element.FindPropertyRelative("_timeSeconds").floatValue;
                float dur = Mathf.Max(
                    element.FindPropertyRelative("_durationSeconds").floatValue, 0.01f);
                HapticWaveformType waveform =
                    (HapticWaveformType)element.FindPropertyRelative("_waveform").enumValueIndex;

                float x = rect.x + (time / duration) * rect.width;
                float w = Mathf.Max(BlockMinWidth, (dur / duration) * rect.width);

                Rect blockRect = new Rect(
                    Mathf.Clamp(x, rect.x, rect.x + rect.width - BlockMinWidth),
                    rect.y + 3f,
                    Mathf.Min(w, rect.width),
                    rect.height - 6f);

                Color color = WaveformColor(waveform);

                if (i == selectedIndex)
                {
                    EditorGUI.DrawRect(
                        new Rect(blockRect.x - 1f, blockRect.y - 1f,
                                 blockRect.width + 2f, blockRect.height + 2f),
                        SelectionColor);
                }

                EditorGUI.DrawRect(blockRect, color);

                GUI.Label(
                    new Rect(blockRect.x + 2f, blockRect.y, blockRect.width - 2f, blockRect.height),
                    waveform.ToString(),
                    new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 8,
                        clipping = TextClipping.Clip,
                        normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
                    });
            }
        }

        private static void HandleTimelineEvents(
            Rect trackRect,
            SerializedProperty keyframesProp,
            float duration,
            SerializedObject rhythmSo,
            ref int selectedIndex)
        {
            Event evt = Event.current;
            if (evt == null || trackRect.width <= 0f || duration <= 0f) return;

            if (evt.type == EventType.MouseDown && trackRect.Contains(evt.mousePosition))
            {
                float time = MouseToTime(trackRect, duration, evt.mousePosition.x);

                if (evt.button == 0)
                {
                    _dragIndex = HitTest(trackRect, keyframesProp, duration, evt.mousePosition);
                    if (_dragIndex >= 0)
                    {
                        selectedIndex = _dragIndex;
                        _dragOffsetSeconds = time -
                            keyframesProp.GetArrayElementAtIndex(_dragIndex)
                                .FindPropertyRelative("_timeSeconds").floatValue;
                        evt.Use();
                        return;
                    }

                    selectedIndex = -1;
                    evt.Use();
                    return;
                }

                if (evt.button == 1)
                {
                    int added = AddKeyframe(keyframesProp, time);
                    if (added >= 0)
                    {
                        selectedIndex = added;
                    }
                    SortKeyframes(keyframesProp, rhythmSo);
                    rhythmSo.ApplyModifiedProperties();
                    evt.Use();
                    return;
                }
            }

            if (evt.type == EventType.MouseDrag && _dragIndex >= 0
                && _dragIndex < keyframesProp.arraySize)
            {
                float time = MouseToTime(trackRect, duration, evt.mousePosition.x);
                float newTime = Snap(Mathf.Max(0f, time - _dragOffsetSeconds));

                SerializedProperty element = keyframesProp.GetArrayElementAtIndex(_dragIndex);
                element.FindPropertyRelative("_timeSeconds").floatValue = newTime;
                rhythmSo.ApplyModifiedProperties();
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseUp && _dragIndex >= 0)
            {
                // Auto-sort only on release so _dragIndex stays valid during the gesture.
                SortKeyframes(keyframesProp, rhythmSo);
                rhythmSo.ApplyModifiedProperties();
                _dragIndex = -1;
                evt.Use();
            }
        }

        private static int HitTest(
            Rect rect,
            SerializedProperty keyframesProp,
            float duration,
            Vector2 mouse)
        {
            for (int i = 0; i < keyframesProp.arraySize; i++)
            {
                SerializedProperty element = keyframesProp.GetArrayElementAtIndex(i);
                float time = element.FindPropertyRelative("_timeSeconds").floatValue;
                float dur = Mathf.Max(
                    element.FindPropertyRelative("_durationSeconds").floatValue, 0.01f);

                float x = rect.x + (time / duration) * rect.width;
                float w = Mathf.Max(BlockMinWidth, (dur / duration) * rect.width);

                Rect blockRect = new Rect(
                    Mathf.Clamp(x, rect.x, rect.x + rect.width - BlockMinWidth),
                    rect.y + 3f,
                    Mathf.Min(w, rect.width),
                    rect.height - 6f);

                if (blockRect.Contains(mouse)) return i;
            }
            return -1;
        }

        private static void SortKeyframes(
            SerializedProperty keyframesProp,
            SerializedObject rhythmSo)
        {
            if (keyframesProp.arraySize < 2) return;

            HapticKeyframe[] items = ReadAllKeyframes(keyframesProp);
            Array.Sort(items, (a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));

            keyframesProp.arraySize = 0;
            for (int i = 0; i < items.Length; i++)
            {
                keyframesProp.InsertArrayElementAtIndex(i);
                WriteKeyframe(
                    keyframesProp.GetArrayElementAtIndex(i), items[i]);
            }

            rhythmSo.ApplyModifiedProperties();
        }

        private static HapticKeyframe[] ReadAllKeyframes(SerializedProperty keyframesProp)
        {
            HapticKeyframe[] items = new HapticKeyframe[keyframesProp.arraySize];
            for (int i = 0; i < items.Length; i++)
            {
                SerializedProperty element = keyframesProp.GetArrayElementAtIndex(i);
                items[i] = new HapticKeyframe(
                    element.FindPropertyRelative("_timeSeconds").floatValue,
                    element.FindPropertyRelative("_durationSeconds").floatValue,
                    element.FindPropertyRelative("_intensity").floatValue,
                    element.FindPropertyRelative("_frequency").floatValue,
                    (HapticWaveformType)element.FindPropertyRelative("_waveform").enumValueIndex);
            }
            return items;
        }

        private static void WriteKeyframe(SerializedProperty element, HapticKeyframe keyframe)
        {
            element.FindPropertyRelative("_timeSeconds").floatValue = keyframe.TimeSeconds;
            element.FindPropertyRelative("_durationSeconds").floatValue = keyframe.DurationSeconds;
            element.FindPropertyRelative("_intensity").floatValue = keyframe.Intensity;
            element.FindPropertyRelative("_frequency").floatValue = keyframe.Frequency;
            element.FindPropertyRelative("_waveform").enumValueIndex = (int)keyframe.Waveform;
        }

        private static float MouseToTime(Rect rect, float duration, float mouseX)
        {
            float t = (mouseX - rect.x) / rect.width * duration;
            return Mathf.Clamp(t, 0f, duration);
        }

        private static float Snap(float time)
        {
            return Mathf.Round(time / SnapStep) * SnapStep;
        }

        private static Color WaveformColor(HapticWaveformType waveform)
        {
            switch (waveform)
            {
                case HapticWaveformType.LightImpact:
                    return LightImpactColor;
                case HapticWaveformType.MediumImpact:
                    return MediumImpactColor;
                case HapticWaveformType.HeavyImpact:
                    return HeavyImpactColor;
                case HapticWaveformType.NotificationSuccess:
                case HapticWaveformType.NotificationWarning:
                case HapticWaveformType.NotificationError:
                    return NotificationColor;
                case HapticWaveformType.SelectionTick:
                    return SelectionTickColor;
                default:
                    return ContinuousColor;
            }
        }

        #endregion
    }
}
