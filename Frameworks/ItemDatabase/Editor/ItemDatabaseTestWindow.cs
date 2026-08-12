using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase.Editor
{
    /// <summary>
    /// Manual test window for the Item Database framework.
    /// Run operations in Play Mode and inspect the output log.
    /// </summary>
    public class ItemDatabaseTestWindow : EditorWindow
    {
        private const int MaxLogLength = 8000;

        private readonly StringBuilder _log = new StringBuilder();
        private Vector2 _scrollPosition;
        private string _testItemId = "gold";
        private int _testCount = 1;
        private bool _autoScroll = true;
        private int _keyCounter;

        [MenuItem("Dev Menu/Tests/Item Database Test")]
        public static void ShowWindow()
        {
            var window = GetWindow<ItemDatabaseTestWindow>("Item DB Test");
            window.minSize = new Vector2(480, 620);
            window.Show();
        }

        private void OnEnable()
        {
            AppendLog("Ready. Enter Play Mode to run operations.");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawStatusHeader();
            }

            EditorGUILayout.Space();
            DrawInputFields();
            EditorGUILayout.Space();
            DrawActionButtons();
            EditorGUILayout.Space();
            DrawOutputLog();
        }

        #region Status Header

        private void DrawStatusHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Item Database", EditorStyles.boldLabel);

                bool inPlay = Application.isPlaying;
                var modeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = inPlay ? Color.green : Color.gray }
                };
                EditorGUILayout.LabelField(inPlay ? "PLAY" : "EDIT", modeStyle);

                GUILayout.FlexibleSpace();
            }

            bool enabled = ItemDatabase.IsEnabled;
            bool directorReady = enabled
                && ItemDatabaseDirector.Instance != null
                && ItemDatabaseDirector.ReadyTask.IsCompleted;

            using (new EditorGUILayout.HorizontalScope())
            {
                AppendBadge("Module", enabled ? "ACTIVE" : "DISABLED",
                    enabled ? new Color(0.2f, 0.7f, 0.2f) : new Color(0.8f, 0.3f, 0.2f));
                AppendBadge("Director", directorReady ? "READY" : "N/A",
                    directorReady ? new Color(0.2f, 0.7f, 0.2f) : new Color(0.5f, 0.5f, 0.5f));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"Definitions: {CountDefinitions()}   Tags: {CountTags()}   "
                + $"Middlewares: {CountMiddlewares()}   Items: {ItemDatabase.Count}",
                EditorStyles.miniLabel);
        }

        private void AppendBadge(string label, string value, Color color)
        {
            EditorGUILayout.LabelField(label,
                new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold },
                GUILayout.Width(60));
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = color },
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(value, style);
        }

        #endregion

        #region Input Fields

        private void DrawInputFields()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Item ID", GUILayout.Width(70));
                _testItemId = EditorGUILayout.TextField(_testItemId);
                EditorGUILayout.LabelField("Count", GUILayout.Width(44));
                _testCount = Mathf.Max(1, EditorGUILayout.IntField(_testCount, GUILayout.Width(60)));
            }
        }

        #endregion

        #region Action Buttons

        private void DrawActionButtons()
        {
            EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            Color old = GUI.backgroundColor;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.35f, 0.75f, 0.35f);
                if (GUILayout.Button("Add Item", GUILayout.Height(30)))
                {
                    AddItemAsync(_testItemId, _testCount);
                }

                GUI.backgroundColor = new Color(0.55f, 0.55f, 0.85f);
                if (GUILayout.Button("Add Gold x1", GUILayout.Height(30)))
                {
                    AddItemAsync("gold", 1);
                }

                if (GUILayout.Button("Add Gold x3", GUILayout.Height(30)))
                {
                    AddItemAsync("gold", 3);
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.85f, 0.55f, 0.35f);
                if (GUILayout.Button("Add Sword x2", GUILayout.Height(30)))
                {
                    AddItemAsync("sword_01", 2);
                }

                GUI.backgroundColor = new Color(0.45f, 0.45f, 0.85f);
                if (GUILayout.Button("Add Gold + Expiry", GUILayout.Height(30)))
                {
                    AddExpiringItemAsync();
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.85f, 0.45f, 0.45f);
                if (GUILayout.Button("Remove Last Added", GUILayout.Height(30)))
                {
                    RemoveLastAddedAsync();
                }

                GUI.backgroundColor = new Color(0.85f, 0.75f, 0.35f);
                if (GUILayout.Button("Force Sync", GUILayout.Height(30)))
                {
                    ForceSyncAsync();
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.65f, 0.45f, 0.85f);
                if (GUILayout.Button("Run Full Suite", GUILayout.Height(32)))
                {
                    RunFullSuiteAsync();
                }
            }

            GUI.backgroundColor = old;
            EditorGUILayout.Space();
        }

        #endregion

        #region Output Log

        private void DrawOutputLog()
        {
            EditorGUI.DrawRect(
                EditorGUILayout.GetControlRect(false, 1f),
                new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                _autoScroll = GUILayout.Toggle(_autoScroll, "Auto-scroll", EditorStyles.miniButton, GUILayout.Width(90));

                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    _log.Clear();
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                EditorGUILayout.LabelField(_log.ToString(), EditorStyles.wordWrappedLabel,
                    GUILayout.MinHeight(200));
                if (_autoScroll)
                {
                    _scrollPosition.y = float.MaxValue;
                }
                EditorGUILayout.EndScrollView();
            }
        }

        #endregion

        #region Operations

        private void AddItemAsync(string itemId, int count)
        {
            if (!RequirePlayMode()) return;

            _ = TaskAddItemAsync(itemId, count);
        }

        private async System.Threading.Tasks.Task TaskAddItemAsync(string itemId, int count)
        {
            try
            {
                AppendLog($"-- Add {itemId} x{count} --");
                for (int i = 0; i < count; i++)
                {
                    string key = NewKey(itemId);
                    await ItemDatabase.AddItemAsync(new InventoryItem(key, itemId));
                    AppendLog($"  + {itemId} key={Short(key)}");
                }

                AppendLog($"  Total '{itemId}' slots: {ItemDatabase.Query().WithDefinition(itemId).Execute().Count}");
                AppendLog($"  Total inventory: {ItemDatabase.Count}");
                AppendStackSummary(itemId);
            }
            catch (Exception ex)
            {
                AppendLog($"  ERROR: {ex.Message}");
            }
        }

        private void AddExpiringItemAsync()
        {
            if (!RequirePlayMode()) return;

            _ = TaskAddExpiringItemAsync();
        }

        private async System.Threading.Tasks.Task TaskAddExpiringItemAsync()
        {
            try
            {
                AppendLog("-- Add gold + 1h expiry --");
                string key = NewKey("gold");
                var item = new InventoryItem(key, "gold");
                await ItemDatabase.AddItemAsync(item);
                await ItemDatabase.SetTagAsync(key, new ExpirableData
                {
                    expiresAt = DateTime.UtcNow.AddHours(1)
                });
                AppendLog($"  + gold(key={Short(key)}) with expiry in 1h");

                var sd = ItemDatabase.GetTag<StackableData>(key);
                AppendLog($"  StackableData.count = {sd?.count ?? -1}");

                var exp = ItemDatabase.GetTag<ExpirableData>(key);
                AppendLog($"  ExpirableData.expiresAt = {exp?.expiresAt:o}");
            }
            catch (Exception ex)
            {
                AppendLog($"  ERROR: {ex.Message}");
            }
        }

        private void RemoveLastAddedAsync()
        {
            if (!RequirePlayMode()) return;

            _ = TaskRemoveLastAddedAsync();
        }

        private async System.Threading.Tasks.Task TaskRemoveLastAddedAsync()
        {
            try
            {
                if (_lastKeys.Count == 0)
                {
                    AppendLog("No tracked keys to remove.");
                    return;
                }

                string key = _lastKeys[_lastKeys.Count - 1];
                _lastKeys.RemoveAt(_lastKeys.Count - 1);

                var item = ItemDatabase.GetItem(key);
                AppendLog($"-- Remove {item?.itemId ?? "?"} key={Short(key)} --");
                await ItemDatabase.RemoveItemAsync(key);
                AppendLog($"  Removed. Total inventory: {ItemDatabase.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"  ERROR: {ex.Message}");
            }
        }

        private void ForceSyncAsync()
        {
            if (!RequirePlayMode()) return;

            _ = TaskForceSyncAsync();
        }

        private async System.Threading.Tasks.Task TaskForceSyncAsync()
        {
            try
            {
                AppendLog("-- Force Sync --");
                await ItemDatabase.ForceSyncAsync();
                AppendLog("  Sync complete.");
            }
            catch (Exception ex)
            {
                AppendLog($"  ERROR: {ex.Message}");
            }
        }

        private void RunFullSuiteAsync()
        {
            if (!RequirePlayMode()) return;

            _ = TaskRunFullSuiteAsync();
        }

        private async System.Threading.Tasks.Task TaskRunFullSuiteAsync()
        {
            AppendLog("================== FULL SUITE ==================");
            await TaskAddItemAsync("gold", 1);
            await TaskAddItemAsync("gold", 1);
            await TaskAddItemAsync("sword_01", 2);
            await TaskAddExpiringItemAsync();
            await TaskVerifyQueriesAsync();
            AppendLog("================== SUITE DONE ==================");
        }

        #endregion

        #region Verification Queries

        private async System.Threading.Tasks.Task TaskVerifyQueriesAsync()
        {
            AppendLog("-- Verification Queries --");

            AppendLog($"  Count = {ItemDatabase.Count}");

            var gold = ItemDatabase.Query().WithDefinition("gold").Execute();
            AppendLog($"  Query('gold').Count = {gold.Count}");

            long totalGold = 0;
            foreach (var gi in gold.Items)
            {
                var sd = ItemDatabase.GetTag<StackableData>(gi.key);
                totalGold += sd?.count ?? 1;
            }
            AppendLog($"  Total gold (stacked) = {totalGold}");

            var swords = ItemDatabase.Query().WithDefinition("sword_01").Execute();
            AppendLog($"  Query('sword_01').Count = {swords.Count}");

            if (swords.Count > 0)
            {
                var swordKey = swords.Items[0].key;
                var wd = new WeaponData { atk = 42 };
                await ItemDatabase.SetTagAsync(swordKey, wd);
                var readBack = ItemDatabase.GetTag<WeaponData>(swordKey);
                AppendLog($"  SetTag/GetTag WeaponData.atk = {readBack?.atk ?? -1}");

                bool isCurrency = ItemDatabase.HasTag<CurrencyTag>(swordKey);
                AppendLog($"  HasTag<CurrencyTag>(sword) = {isCurrency}");
            }

            if (gold.Count > 0)
            {
                var goldKey = gold.Items[0].key;
                bool isCurrency = ItemDatabase.HasTag<CurrencyTag>(goldKey);
                AppendLog($"  HasTag<CurrencyTag>(gold) = {isCurrency}");

                bool isStackable = ItemDatabase.GetTag<StackableData>(goldKey) != null;
                AppendLog($"  GetTag<StackableData>(gold) != null = {isStackable}");
            }

            var def = ItemDatabase.GetDefinition("gold");
            AppendLog($"  GetDefinition('gold') = {(def != null ? def.ItemId : "null")}");

            AppendLog($"  GetTagOwner(typeof(CurrencyTag)) = {ItemDatabase.GetTagOwner(typeof(CurrencyTag)) ?? "null"}");
            AppendLog($"  GetDefinitionOwner('gold') = {ItemDatabase.GetDefinitionOwner("gold") ?? "null"}");
        }

        private void AppendStackSummary(string itemId)
        {
            var query = ItemDatabase.Query().WithDefinition(itemId).Execute();
            foreach (var item in query.Items)
            {
                var sd = ItemDatabase.GetTag<StackableData>(item.key);
                if (sd != null)
                {
                    AppendLog($"    stack key={Short(item.key)} count={sd.count}");
                }
            }
        }

        #endregion

        #region Helpers

        private readonly List<string> _lastKeys = new List<string>();

        private string NewKey(string itemId)
        {
            _keyCounter++;
            string key = $"{itemId}_test_{DateTime.Now:HHmmss}_{_keyCounter}";
            _lastKeys.Add(key);
            return key;
        }

        private string Short(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            return key.Length <= 24 ? key : key.Substring(0, 24);
        }

        private bool RequirePlayMode()
        {
            if (Application.isPlaying) return true;

            AppendLog("NOT in Play Mode — enter Play Mode first.");
            return false;
        }

        private int CountDefinitions()
        {
            var config = ItemDatabaseConfiguration.Instance;
            return config != null ? config.ItemDefinitions.Length : 0;
        }

        private int CountTags()
        {
            var config = ItemDatabaseConfiguration.Instance;
            return config != null ? config.TagDefinitions.Length : 0;
        }

        private int CountMiddlewares()
        {
            var config = ItemDatabaseConfiguration.Instance;
            return config != null ? config.MiddlewareTypeNames.Length : 0;
        }

        private void AppendLog(string line)
        {
            if (_log.Length > 0) _log.AppendLine();
            _log.Append(line);

            if (_log.Length > MaxLogLength)
            {
                _log.Remove(0, _log.Length - MaxLogLength);
            }

            Repaint();
        }

        #endregion
    }
}
