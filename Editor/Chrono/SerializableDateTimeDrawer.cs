using System;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono.Editor
{
    [CustomPropertyDrawer(typeof(SerializableDateTime))]
    public class SerializableDateTimeDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 24f;
        private const float Padding = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty ticksProp = property.FindPropertyRelative("_ticks");
            SerializedProperty kindProp = property.FindPropertyRelative("_kindValue");

            if (ticksProp == null || kindProp == null)
            {
                EditorGUI.LabelField(position, label.text, "SerializableDateTime (error)");
                return;
            }

            long ticks = ticksProp.longValue;
            DateTimeKind kind = (DateTimeKind)kindProp.intValue;
            DateTime current = ticks == 0
                ? DateTime.MinValue
                : new DateTime(ticks, kind);

            Rect labelRect = new Rect(
                position.x,
                position.y,
                EditorGUIUtility.labelWidth,
                position.height
            );

            float buttonAreaWidth = position.width - EditorGUIUtility.labelWidth;
            Rect fieldRect = new Rect(
                labelRect.xMax,
                position.y,
                buttonAreaWidth - ButtonWidth - Padding,
                position.height
            );

            Rect buttonRect = new Rect(
                fieldRect.xMax + Padding,
                position.y,
                ButtonWidth,
                position.height
            );

            EditorGUI.LabelField(labelRect, label);

            string displayText = ticks == 0
                ? "(not set)"
                : FormatDateTime(current);

            GUI.SetNextControlName(property.propertyPath + "_field");
            string newText = EditorGUI.TextField(fieldRect, displayText);

            if (newText != displayText)
            {
                TryParseAndApply(newText, current, ticksProp, kindProp);
            }

            if (GUI.Button(buttonRect, "\u25BC", EditorStyles.miniButton))
            {
                DateTimePickerPopup.Show(
                    buttonRect,
                    current,
                    ticksProp,
                    kindProp
                );
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        private static void TryParseAndApply(
            string text,
            DateTime fallback,
            SerializedProperty ticksProp,
            SerializedProperty kindProp
        )
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ApplyDateTime(DateTime.MinValue, ticksProp, kindProp);
                return;
            }

            if (DateTime.TryParse(text, out DateTime parsed))
            {
                ApplyDateTime(parsed, ticksProp, kindProp);
            }
            else
            {
                ApplyDateTime(fallback, ticksProp, kindProp);
            }
        }

        internal static void ApplyDateTime(
            DateTime value,
            SerializedProperty ticksProp,
            SerializedProperty kindProp
        )
        {
            ticksProp.longValue = value.Ticks;
            kindProp.intValue = (int)value.Kind;
            ticksProp.serializedObject.ApplyModifiedProperties();
        }

        internal static string FormatDateTime(DateTime dt)
        {
            string kindSuffix = dt.Kind switch
            {
                DateTimeKind.Utc => " UTC",
                DateTimeKind.Local => " Local",
                _ => ""
            };

            return dt.ToString("yyyy-MM-dd HH:mm:ss.fff") + kindSuffix;
        }
    }

    internal sealed class DateTimePickerPopup : PopupWindowContent
    {
        private SerializedProperty _ticksProp;
        private SerializedProperty _kindProp;

        private DateTime _initialValue;
        private DateTime _currentValue;
        private DateTimeKind _currentKind;

        private int _selectedYear;
        private int _selectedMonth;
        private int _selectedDay;
        private int _selectedHour;
        private int _selectedMinute;
        private int _selectedSecond;
        private int _selectedMillisecond;
        private int _selectedKindIndex;

        private static readonly string[] KindLabels = { "Utc", "Local", "Unspecified" };
        private static readonly DateTimeKind[] KindValues =
            { DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified };

        private const float WindowWidth = 340f;
        private const float WindowHeight = 370f;
        private const float CellSize = 38f;
        private const float NavWidth = 28f;
        private const float YearFieldWidth = 48f;
        private const float MonthLabelWidth = 58f;
        private const float TimeFieldWidth = 40f;
        private const float ColonWidth = 8f;
        private const float ButtonHeight = 24f;
        private const float Pad = 8f;
        private const float HalfPad = 4f;

        private static readonly string[] DayHeaders =
            { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };

        private static readonly GUIStyle CenteredLabel;

        static DateTimePickerPopup()
        {
            CenteredLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }

        public static void Show(
            Rect activatorRect,
            DateTime current,
            SerializedProperty ticksProp,
            SerializedProperty kindProp
        )
        {
            var popup = new DateTimePickerPopup
            {
                _ticksProp = ticksProp,
                _kindProp = kindProp,
                _initialValue = current,
                _currentValue = current,
                _currentKind = current.Kind
            };

            popup.UpdateFieldsFromValue();
            PopupWindow.Show(activatorRect, popup);
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(WindowWidth, WindowHeight);
        }

        public override void OnGUI(Rect rect)
        {
            DrawTitle();
            DrawMonthYearSelector();
            DrawCalendarGrid();
            DrawTimeFields();
            DrawKindSelector();
            DrawFooterButtons();
        }

        private void UpdateFieldsFromValue()
        {
            _selectedYear = _currentValue.Year;
            _selectedMonth = _currentValue.Month;
            _selectedDay = _currentValue.Day;
            _selectedHour = _currentValue.Hour;
            _selectedMinute = _currentValue.Minute;
            _selectedSecond = _currentValue.Second;
            _selectedMillisecond = _currentValue.Millisecond;

            _selectedKindIndex = Array.IndexOf(KindValues, _currentKind);
            if (_selectedKindIndex < 0)
            {
                _selectedKindIndex = 2;
            }
        }

        private void ApplyFieldsToValue()
        {
            int year = Mathf.Clamp(_selectedYear, 1, 9999);
            int month = Mathf.Clamp(_selectedMonth, 1, 12);
            int day = Mathf.Clamp(_selectedDay, 1, DateTime.DaysInMonth(year, month));
            int hour = Mathf.Clamp(_selectedHour, 0, 23);
            int minute = Mathf.Clamp(_selectedMinute, 0, 59);
            int second = Mathf.Clamp(_selectedSecond, 0, 59);
            int ms = Mathf.Clamp(_selectedMillisecond, 0, 999);

            _selectedYear = year;
            _selectedMonth = month;
            _selectedDay = day;
            _selectedHour = hour;
            _selectedMinute = minute;
            _selectedSecond = second;
            _selectedMillisecond = ms;

            _currentValue = new DateTime(year, month, day, hour, minute, second, ms, _currentKind);
        }

        #region Drawing

        private void DrawTitle()
        {
            GUILayout.Space(HalfPad);

            EditorGUILayout.LabelField(
                "Select DateTime",
                EditorStyles.boldLabel
            );

            Rect divider = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(divider, new Color(0.5f, 0.5f, 0.5f, 0.3f));

            GUILayout.Space(HalfPad);
        }

        private void DrawMonthYearSelector()
        {
            // Total width needed: Nav(28) + MonthLabel(58) + Nav(28) + Gap(8) + Nav(28) + YearField(48) + Nav(28) = 226
            // Window is 340, so remaining = 114 → 57 padding each side
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // ◄ Month
            if (GUILayout.Button("\u25C0", GUILayout.Width(NavWidth)))
            {
                _selectedMonth--;
                if (_selectedMonth < 1) { _selectedMonth = 12; _selectedYear--; }
                ApplyFieldsToValue();
                UpdateFieldsFromValue();
            }

            string[] monthNames = System.Globalization.CultureInfo
                .CurrentCulture.DateTimeFormat.AbbreviatedMonthNames;

            EditorGUILayout.LabelField(
                monthNames[_selectedMonth - 1],
                EditorStyles.boldLabel,
                GUILayout.Width(MonthLabelWidth)
            );

            // Month ►
            if (GUILayout.Button("\u25B6", GUILayout.Width(NavWidth)))
            {
                _selectedMonth++;
                if (_selectedMonth > 12) { _selectedMonth = 1; _selectedYear++; }
                ApplyFieldsToValue();
                UpdateFieldsFromValue();
            }

            GUILayout.Space(8);

            // ◄ Year
            if (GUILayout.Button("\u25C0", GUILayout.Width(NavWidth)))
            {
                _selectedYear--;
                ApplyFieldsToValue();
                UpdateFieldsFromValue();
            }

            string yearStr = EditorGUILayout.TextField(
                _selectedYear.ToString(),
                GUILayout.Width(YearFieldWidth)
            );

            if (int.TryParse(yearStr, out int parsedYear)
                && parsedYear != _selectedYear
                && parsedYear >= 1
                && parsedYear <= 9999)
            {
                _selectedYear = parsedYear;
                ApplyFieldsToValue();
                UpdateFieldsFromValue();
            }

            // Year ►
            if (GUILayout.Button("\u25B6", GUILayout.Width(NavWidth)))
            {
                _selectedYear++;
                ApplyFieldsToValue();
                UpdateFieldsFromValue();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(HalfPad);
        }

        private void DrawCalendarGrid()
        {
            // Day headers — center the grid
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            foreach (string header in DayHeaders)
            {
                GUILayout.Label(header, CenteredLabel, GUILayout.Width(CellSize));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            DateTime firstOfMonth = new DateTime(
                _selectedYear, _selectedMonth, 1, 0, 0, 0, _currentKind
            );
            int startDayOfWeek = (int)firstOfMonth.DayOfWeek;
            int daysInMonth = DateTime.DaysInMonth(_selectedYear, _selectedMonth);

            int dayCounter = 1;
            for (int row = 0; row < 6; row++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                for (int col = 0; col < 7; col++)
                {
                    if ((row == 0 && col < startDayOfWeek) || dayCounter > daysInMonth)
                    {
                        // Empty cell — use Label to match Button height
                        GUILayout.Label("", GUILayout.Width(CellSize));
                        continue;
                    }

                    bool isToday = dayCounter == _selectedDay;

                    Color oldColor = GUI.backgroundColor;
                    if (isToday)
                    {
                        GUI.backgroundColor = new Color(0.25f, 0.5f, 0.9f);
                    }

                    if (GUILayout.Button(
                            dayCounter.ToString(),
                            GUILayout.Width(CellSize),
                            GUILayout.Height(22f)))
                    {
                        _selectedDay = dayCounter;
                        ApplyFieldsToValue();
                        UpdateFieldsFromValue();
                    }

                    GUI.backgroundColor = oldColor;
                    dayCounter++;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (dayCounter > daysInMonth)
                {
                    break;
                }
            }

            GUILayout.Space(HalfPad);
        }

        private void DrawTimeFields()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Time", GUILayout.Width(32));

            _selectedHour = IntField(_selectedHour, 0, 23, TimeFieldWidth);
            GUILayout.Label(":", GUILayout.Width(ColonWidth));
            _selectedMinute = IntField(_selectedMinute, 0, 59, TimeFieldWidth);
            GUILayout.Label(":", GUILayout.Width(ColonWidth));
            _selectedSecond = IntField(_selectedSecond, 0, 59, TimeFieldWidth);
            GUILayout.Label(".", GUILayout.Width(ColonWidth));
            _selectedMillisecond = IntField(
                _selectedMillisecond, 0, 999, 46
            );

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            ApplyFieldsToValue();
            GUILayout.Space(HalfPad);
        }

        private void DrawKindSelector()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Kind", GUILayout.Width(32));

            int newKindIndex = GUILayout.SelectionGrid(
                _selectedKindIndex,
                KindLabels,
                3,
                GUILayout.Width(210)
            );

            if (newKindIndex != _selectedKindIndex)
            {
                _selectedKindIndex = newKindIndex;
                _currentKind = KindValues[_selectedKindIndex];
                ApplyFieldsToValue();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(Pad);
        }

        private void DrawFooterButtons()
        {
            Rect divider = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(divider, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            GUILayout.Space(HalfPad);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("OK", GUILayout.Width(80), GUILayout.Height(ButtonHeight)))
            {
                SerializableDateTimeDrawer.ApplyDateTime(
                    _currentValue,
                    _ticksProp,
                    _kindProp
                );
                editorWindow.Close();
            }

            GUILayout.Space(12);

            if (GUILayout.Button(
                    "Reset",
                    GUILayout.Width(80),
                    GUILayout.Height(ButtonHeight)))
            {
                _currentValue = _initialValue;
                _currentKind = _initialValue.Kind;
                UpdateFieldsFromValue();
            }

            GUILayout.Space(12);

            if (GUILayout.Button(
                    "Cancel",
                    GUILayout.Width(80),
                    GUILayout.Height(ButtonHeight)))
            {
                editorWindow.Close();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(HalfPad);
        }

        #endregion

        #region Helpers

        private static int IntField(int value, int min, int max, float width)
        {
            string text = GUILayout.TextField(
                value.ToString(min > 99 ? "D3" : "D2"),
                GUILayout.Width(width)
            );

            if (int.TryParse(text, out int parsed))
            {
                return Mathf.Clamp(parsed, min, max);
            }

            return value;
        }

        #endregion
    }
}
