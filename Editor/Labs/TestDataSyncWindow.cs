using System;
using Com.Hapiga.Scheherazade.Common.DataSync;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync.Editor
{
    public class TestDataSyncWindow : EditorWindow
    {
        private const string DefaultKey = "test_sync_data";

        private const int MaxStatusLength = 3000;

        private string _key = DefaultKey;
        private string _playerName = "Hero";
        private int _score = 42;
        private float _playTime = 120.5f;
        private string _status = "Ready.";
        private bool _exists;
        private Vector2 _scrollPosition;

        [MenuItem("Dev Menu/Tests/Test Data Window")]
        public static void ShowWindow()
        {
            TestDataSyncWindow window = GetWindow<TestDataSyncWindow>("Data Sync Test");
            window.minSize = new Vector2(400, 560);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshExists();
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();
            DrawDataFields();
            DrawActionButtons();
            DrawOutputLog();

            EditorGUILayout.EndScrollView();
        }

        #region Header

        private void DrawHeader()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Data Sync Test",
                    EditorStyles.boldLabel,
                    GUILayout.Width(120)
                );

                bool inPlayMode = Application.isPlaying;
                var modeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = inPlayMode ? Color.green : Color.gray }
                };
                EditorGUILayout.LabelField(
                    inPlayMode ? "Play Mode" : "Edit Mode",
                    modeStyle
                );
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Key", GUILayout.Width(30));
                _key = EditorGUILayout.TextField(_key);

                if (GUILayout.Button("?", GUILayout.Width(24)))
                {
                    RefreshExists();
                }
            }

            string existsMessage = _exists
                ? $"  Data exists for '{_key}'."
                : $"  No data found for '{_key}'.";
            EditorGUILayout.LabelField(existsMessage, EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUI.DrawRect(
                EditorGUILayout.GetControlRect(false, 1f),
                new Color(0.5f, 0.5f, 0.5f, 0.3f)
            );
            EditorGUILayout.Space();
        }

        #endregion

        #region Data Fields

        private void DrawDataFields()
        {
            EditorGUILayout.LabelField("Test Data", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _playerName = EditorGUILayout.TextField("Player Name", _playerName);
            _score = EditorGUILayout.IntField("Score", _score);
            _playTime = EditorGUILayout.FloatField("Play Time (s)", _playTime);

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);
                var preview = BuildData();
                EditorGUILayout.LabelField(
                    preview.ToString(),
                    EditorStyles.wordWrappedLabel
                );
            }

            EditorGUILayout.Space();
        }

        #endregion

        #region Buttons

        private void DrawActionButtons()
        {
            EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            Color oldColor = GUI.backgroundColor;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.35f, 0.75f, 0.35f);
                if (GUILayout.Button("Save", GUILayout.Height(32)))
                {
                    DoSave();
                }

                GUI.backgroundColor = new Color(0.35f, 0.55f, 0.85f);
                if (GUILayout.Button("Load", GUILayout.Height(32)))
                {
                    DoLoad();
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
                if (GUILayout.Button("Delete", GUILayout.Height(30)))
                {
                    DoDelete();
                }

                GUI.backgroundColor = new Color(0.55f, 0.55f, 0.55f);
                if (GUILayout.Button("Check Exists", GUILayout.Height(30)))
                {
                    RefreshExists();
                }
            }

            EditorGUILayout.Space();

            GUI.backgroundColor = new Color(0.65f, 0.45f, 0.85f);
            if (GUILayout.Button("Run Full Cycle", GUILayout.Height(30)))
            {
                DoFullCycle();
            }

            GUI.backgroundColor = oldColor;
            EditorGUILayout.Space();
        }

        #endregion

        #region Output Log

        private void DrawOutputLog()
        {
            EditorGUI.DrawRect(
                EditorGUILayout.GetControlRect(false, 1f),
                new Color(0.5f, 0.5f, 0.5f, 0.3f)
            );
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    _status = "Ready.";
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    _status,
                    EditorStyles.wordWrappedLabel,
                    GUILayout.MinHeight(80)
                );
            }
        }

        #endregion

        #region Operations

        private TestSyncData BuildData()
        {
            return new TestSyncData
            {
                playerName = _playerName,
                score = _score,
                playTime = _playTime
            };
        }

        private async void DoSave()
        {
            if (!RequirePlayMode()) return;

            try
            {
                TestSyncData data = BuildData();
                AppendStatus($"Saving '{_key}'...");
                AppendStatus($"  {data}");

                await DataSync.SaveAsync(_key, data);

                AppendStatus("Save complete.");
                RefreshExists();
            }
            catch (Exception ex)
            {
                AppendStatus($"Save failed: {ex.Message}");
                Debug.LogError($"[TestDataSync] Save error: {ex}");
            }
        }

        private async void DoLoad()
        {
            if (!RequirePlayMode()) return;

            try
            {
                if (!await DataSync.ExistsAsync(_key))
                {
                    AppendStatus($"Key '{_key}' not found.");
                    RefreshExists();
                    return;
                }

                AppendStatus($"Loading '{_key}'...");
                TestSyncData loaded = await DataSync.LoadAsync<TestSyncData>(_key);

                _playerName = loaded.playerName;
                _score = loaded.score;
                _playTime = loaded.playTime;

                AppendStatus($"Loaded: {loaded}");
                RefreshExists();
                Repaint();
            }
            catch (Exception ex)
            {
                AppendStatus($"Load failed: {ex.Message}");
                Debug.LogError($"[TestDataSync] Load error: {ex}");
            }
        }

        private async void DoDelete()
        {
            if (!RequirePlayMode()) return;

            try
            {
                AppendStatus($"Deleting '{_key}'...");
                await DataSync.DeleteAsync(_key);
                AppendStatus("Deleted.");
                RefreshExists();
            }
            catch (Exception ex)
            {
                AppendStatus($"Delete failed: {ex.Message}");
                Debug.LogError($"[TestDataSync] Delete error: {ex}");
            }
        }

        private async void RefreshExists()
        {
            if (!Application.isPlaying)
            {
                _exists = false;
                return;
            }

            try
            {
                _exists = await DataSync.ExistsAsync(_key);
                Repaint();
            }
            catch
            {
                _exists = false;
            }
        }

        private async void DoFullCycle()
        {
            if (!RequirePlayMode()) return;

            AppendStatus("=== Full Cycle Test ===");

            try
            {
                TestSyncData original = BuildData();

                AppendStatus($"1. Saving: {original}");
                await DataSync.SaveAsync(_key, original);

                bool exists = await DataSync.ExistsAsync(_key);
                AppendStatus($"2. Exists: {(exists ? "YES" : "NO")}");

                TestSyncData loaded = await DataSync.LoadAsync<TestSyncData>(_key);
                bool matches = loaded.playerName == original.playerName
                               && loaded.score == original.score
                               && Mathf.Approximately(loaded.playTime, original.playTime);
                AppendStatus($"3. Loaded: {loaded}");
                AppendStatus($"   Round-trip: {(matches ? "MATCH" : "MISMATCH")}");

                await DataSync.DeleteAsync(_key);
                AppendStatus("4. Deleted.");

                exists = await DataSync.ExistsAsync(_key);
                AppendStatus($"5. Gone: {(exists ? "STILL EXISTS" : "CONFIRMED")}");

                AppendStatus("=== Complete ===");
                RefreshExists();
            }
            catch (Exception ex)
            {
                AppendStatus($"Cycle failed: {ex.Message}");
                Debug.LogError($"[TestDataSync] Cycle error: {ex}");
            }
        }

        #endregion

        #region Helpers

        private static bool RequirePlayMode()
        {
            if (Application.isPlaying) return true;
            Debug.LogWarning("[TestDataSync] Enter Play Mode first.");
            return false;
        }

        private void AppendStatus(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _status = $"[{timestamp}] {message}\n{_status}";

            if (_status.Length > MaxStatusLength)
            {
                int cutoff = _status.LastIndexOf('\n', MaxStatusLength);
                _status = cutoff > 0
                    ? _status.Substring(0, cutoff)
                    : _status.Substring(0, MaxStatusLength);
            }

            Repaint();
        }

        #endregion
    }
}
