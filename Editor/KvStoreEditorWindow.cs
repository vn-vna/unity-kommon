using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.DataSync;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync.Editor
{
    public class KvStoreEditorWindow : EditorWindow
    {
        #region Private Fields

        private string _budgetName = "settings";
        private KvBudget _activeBudget;
        private string _activeBudgetName;

        private string _newKey = "";
        private string _newValue = "";

        private Vector2 _scrollPosition;
        private bool _isBusy;
        private string _busyMessage = "";

        private static readonly Dictionary<string, string> _entryBuffer
            = new Dictionary<string, string>();

        #endregion

        #region Menu Item

        [MenuItem("Tools/Scheherazade/KV Store Editor")]
        public static void OpenWindow()
        {
            KvStoreEditorWindow window = GetWindow<KvStoreEditorWindow>(
                "KV Store Editor"
            );
            window.minSize = new Vector2(420, 360);
            window.Show();
        }

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                DrawEditModeHint();
                return;
            }

            if (_isBusy)
            {
                EditorGUILayout.LabelField(
                    _busyMessage,
                    EditorStyles.centeredGreyMiniLabel
                );
                return;
            }

            DrawBudgetSelector();
            EditorGUILayout.Space();

            if (_activeBudget == null || !_activeBudget.IsLoaded)
            {
                EditorGUILayout.HelpBox(
                    "No budget open. Select a budget name and click Open.",
                    MessageType.Info
                );
                return;
            }

            DrawActiveBudgetInfo();
            EditorGUILayout.Space();
            DrawEntryTable();
            EditorGUILayout.Space();
            DrawAddEntry();
            EditorGUILayout.Space();
            DrawActionsBar();
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            // Periodically repaint to reflect external changes
            if (_activeBudget != null && _activeBudget.IsLoaded
                && EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        #endregion

        #region GUI Sections

        private void DrawEditModeHint()
        {
            GUILayout.Label("KV Store Editor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Enter Play Mode to view and edit KV Store budgets.",
                MessageType.Info
            );

            EditorGUILayout.Space();

            GUILayout.Label("Budgets on Disk (read-only preview)",
                EditorStyles.boldLabel);

            string[] files = GetBudgetFilesOnDisk();
            if (files.Length == 0)
            {
                EditorGUILayout.LabelField("No budget files found on disk.");
            }
            else
            {
                foreach (string file in files)
                {
                    EditorGUILayout.LabelField($"  {file}");
                }
            }
        }

        private void DrawBudgetSelector()
        {
            EditorGUILayout.BeginHorizontal();
            _budgetName = EditorGUILayout.TextField("Budget", _budgetName);

            GUI.enabled = !string.IsNullOrEmpty(_budgetName);
            if (GUILayout.Button("Open", GUILayout.Width(60)))
            {
                _ = OpenBudgetAsync(_budgetName);
            }
            GUI.enabled = true;

            if (_activeBudget != null && GUILayout.Button("Close",
                GUILayout.Width(60)))
            {
                _ = CloseBudgetAsync();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActiveBudgetInfo()
        {
            EditorGUILayout.LabelField(
                $"Budget: {_activeBudgetName}",
                EditorStyles.boldLabel
            );

            EditorGUILayout.LabelField(
                $"Entries: {_activeBudget.Keys.Count}"
            );

            // Show adapter features
            try
            {
                ISaveAdapter adapter = DataSyncDirector.Instance
                    .ResolveAdapter();

                EditorGUILayout.LabelField(
                    $"Adapter: {adapter.AdapterId}"
                );
                EditorGUILayout.LabelField(
                    $"Features: {adapter.SupportedFeatures}"
                );
                EditorGUILayout.LabelField(
                    $"Available: {adapter.IsAvailable}"
                );
            }
            catch { }
        }

        private void DrawEntryTable()
        {
            if (_activeBudget.Keys.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "No entries.",
                    EditorStyles.centeredGreyMiniLabel
                );
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Height(180)
            );

            var keysToRemove = new List<string>();

            foreach (string key in _activeBudget.Keys)
            {
                EditorGUILayout.BeginHorizontal();

                // Key column
                EditorGUILayout.LabelField(key,
                    GUILayout.Width(140));

                // Value column (editable)
                if (!_entryBuffer.TryGetValue(key, out string buffer))
                {
                    buffer = _activeBudget.Get<string>(key, "");
                    _entryBuffer[key] = buffer;
                }

                string newValue = EditorGUILayout.TextField(buffer);
                if (newValue != buffer)
                    _entryBuffer[key] = newValue;

                // Save button
                if (_entryBuffer.ContainsKey(key)
                    && _entryBuffer[key] != _activeBudget.Get<string>(key, ""))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(50)))
                    {
                        string keyCopy = key;
                        string valCopy = _entryBuffer[key];
                        _ = SaveEntryAsync(keyCopy, valCopy);
                    }
                }

                // Delete button
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    keysToRemove.Add(key);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // Process deletions after iteration
            foreach (string key in keysToRemove)
            {
                _entryBuffer.Remove(key);
                _ = DeleteEntryAsync(key);
            }
        }

        private void DrawAddEntry()
        {
            EditorGUILayout.BeginHorizontal();
            _newKey = EditorGUILayout.TextField("Key", _newKey,
                GUILayout.Width(140));
            _newValue = EditorGUILayout.TextField("Value", _newValue);

            GUI.enabled = !string.IsNullOrEmpty(_newKey)
                && !_activeBudget.HasKey(_newKey);

            if (GUILayout.Button("Add", GUILayout.Width(50)))
            {
                string key = _newKey;
                string val = _newValue;
                _ = AddEntryAsync(key, val);
                _newKey = "";
                _newValue = "";
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionsBar()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Force Sync"))
            {
                _ = ForceSyncAsync();
            }

            if (GUILayout.Button("Refresh"))
            {
                _entryBuffer.Clear();
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Async Operations

        private async Task OpenBudgetAsync(string name)
        {
            SetBusy($"Opening budget '{name}'...");
            try
            {
                KvBudget budget = await KvStore.OpenAsync(name);
                _activeBudget = budget;
                _activeBudgetName = name;
                _entryBuffer.Clear();
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KV Store Editor] Failed to open budget '{name}': {ex}"
                );
            }
            finally
            {
                ClearBusy();
            }
        }

        private async Task CloseBudgetAsync()
        {
            if (_activeBudgetName == null) return;

            SetBusy($"Closing budget '{_activeBudgetName}'...");
            try
            {
                await KvStore.CloseAsync(_activeBudgetName);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KV Store Editor] Failed to close budget: {ex}"
                );
            }
            finally
            {
                _activeBudget = null;
                _activeBudgetName = null;
                _entryBuffer.Clear();
                ClearBusy();
            }
        }

        private async Task SaveEntryAsync(string key, string value)
        {
            SetBusy("Saving...");
            try
            {
                await _activeBudget.SetAsync(key, value);
                _entryBuffer.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KV Store Editor] Failed to save '{key}': {ex}"
                );
            }
            finally
            {
                ClearBusy();
            }
        }

        private async Task DeleteEntryAsync(string key)
        {
            SetBusy("Deleting...");
            try
            {
                await _activeBudget.DeleteAsync(key);
                _entryBuffer.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KV Store Editor] Failed to delete '{key}': {ex}"
                );
            }
            finally
            {
                ClearBusy();
            }
        }

        private async Task AddEntryAsync(string key, string value)
        {
            SetBusy("Adding...");
            try
            {
                await _activeBudget.SetAsync(key, value);
                _entryBuffer.Remove(key);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KV Store Editor] Failed to add '{key}': {ex}"
                );
            }
            finally
            {
                ClearBusy();
            }
        }

        private async Task ForceSyncAsync()
        {
            if (_activeBudget == null) return;

            SetBusy("Force syncing...");
            try
            {
                await _activeBudget.ForceSyncAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[KV Store Editor] Force sync failed: {ex}"
                );
            }
            finally
            {
                ClearBusy();
            }
        }

        #endregion

        #region Busy State

        private void SetBusy(string message)
        {
            _isBusy = true;
            _busyMessage = message;
            Repaint();
        }

        private void ClearBusy()
        {
            _isBusy = false;
            _busyMessage = "";
            Repaint();
        }

        #endregion

        #region Edit Mode Helpers

        private void HandlePlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                _activeBudget = null;
                _activeBudgetName = null;
                _entryBuffer.Clear();
            }
        }

        private static string[] GetBudgetFilesOnDisk()
        {
            string path = System.IO.Path.Combine(
                Application.persistentDataPath,
                "SaveData"
            );

            if (!System.IO.Directory.Exists(path))
                return Array.Empty<string>();

            string[] files = System.IO.Directory.GetFiles(
                path,
                "kv_*.dat"
            );

            for (int i = 0; i < files.Length; i++)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(
                    files[i]
                );

                if (name.StartsWith("kv_"))
                    files[i] = name.Substring(3);
                else
                    files[i] = name;
            }

            return files;
        }

        #endregion
    }
}
