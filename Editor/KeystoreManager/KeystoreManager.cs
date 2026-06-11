using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
using UnityEditor.Build.Profile;
#endif

namespace Com.Hapiga.Scheherazade.Common.KeystoreManager
{
    public class KeystoreManagerWindow : EditorWindow
    {
        private const string PrefsKeyPrefix = "Scheherazade_Keystore_";
        private const string ProfilePrefsKeySuffix = "_profile_";

        private KeystoreData _storedData;
        private KeystoreData _editingData;
        private ConflictState _conflictState;
        private bool _foldoutGlobal = true;

#if UNITY_6000_0_OR_NEWER
        private bool _foldoutProfiles = true;
        private List<BuildProfile> _buildProfiles;
        private Dictionary<string, ProfileOverrideEntry> _profileOverrides;

        private struct ProfileOverrideEntry
        {
            public KeystoreData stored;
            public KeystoreData current;
            public bool hasConflict;
        }
#endif

        private enum ConflictState
        {
            None,
            NoStoredData,
            Synced,
            Conflict
        }

        [InitializeOnLoadMethod]
        private static void Init()
        {
            ApplyStoredToProject();
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private static void OnProjectChanged()
        {
            ApplyStoredToProject();
        }

        private static void ApplyStoredToProject()
        {
            string globalJson = LoadRawString(PrefsKeyPrefix + GetProjectKey());
            if (string.IsNullOrEmpty(globalJson))
                return;

            KeystoreData data = Deserialize(globalJson);
            if (data == null)
                return;

#if UNITY_6000_0_OR_NEWER
            ApplyToProjectSettings(data);

            string activeProfileGuid = null;
            var activeProfile = BuildProfile.GetActiveBuildProfile();
            if (activeProfile != null)
                activeProfileGuid = AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(activeProfile));

            foreach (var profile in GetAllBuildProfiles())
            {
                string profileGuid = AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(profile));
                string profileJson = LoadRawString(
                    PrefsKeyPrefix + GetProjectKey() + ProfilePrefsKeySuffix + profileGuid);

                if (!string.IsNullOrEmpty(profileJson))
                {
                    KeystoreData profileData = Deserialize(profileJson);
                    if (profileData != null)
                    {
                        BuildProfile.SetActiveBuildProfile(profile);
                        ApplyToProjectSettings(profileData);
                    }
                }
            }

            if (activeProfile != null)
                BuildProfile.SetActiveBuildProfile(activeProfile);
#else
            ApplyToProjectSettings(data);
#endif
            Debug.Log("[KeystoreManager] Keystore settings applied to project.");
        }

        private static void ApplyToProjectSettings(KeystoreData data)
        {
            PlayerSettings.Android.keystoreName = data.keystoreName ?? "";
            PlayerSettings.Android.keystorePass = data.keystorePass ?? "";
            PlayerSettings.Android.keyaliasName = data.keyaliasName ?? "";
            PlayerSettings.Android.keyaliasPass = data.keyaliasPass ?? "";
        }

#if UNITY_6000_0_OR_NEWER
        private static List<BuildProfile> GetAllBuildProfiles()
        {
            var profiles = new List<BuildProfile>();
            string[] guids = AssetDatabase.FindAssets("t:BuildProfile");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (profile != null)
                    profiles.Add(profile);
            }
            return profiles;
        }
#endif

        [MenuItem("Dev Menu/Tools/Keystore Helper")]
        private static void ShowWindow()
        {
            var window = GetWindow<KeystoreManagerWindow>(true, "Keystore Helper");
            window.minSize = new Vector2(520, 440);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshData();
        }

        private void OnFocus()
        {
            RefreshData();
        }

        private void RefreshData()
        {
            try
            {
                _storedData = LoadStoredData();
                var currentData = LoadCurrentProjectData();

                if (_storedData != null)
                {
                    _editingData = _storedData.Clone();
                    _conflictState = _storedData.Equals(currentData) ? ConflictState.Synced : ConflictState.Conflict;
                }
                else
                {
                    _editingData = currentData.Clone();
                    _conflictState = ConflictState.NoStoredData;
                }

#if UNITY_6000_0_OR_NEWER
                _buildProfiles = GetAllBuildProfiles();
                _profileOverrides = BuildProfileOverridesMap();
#endif
            }
            catch (Exception e)
            {
                Debug.LogError("[KeystoreManager] Error loading data: " + e.Message);
                _editingData = new KeystoreData();
                _conflictState = ConflictState.NoStoredData;
            }
        }

        private static KeystoreData LoadStoredData()
        {
            string json = LoadRawString(PrefsKeyPrefix + GetProjectKey());
            if (string.IsNullOrEmpty(json))
                return null;
            return Deserialize(json);
        }

        private static KeystoreData LoadCurrentProjectData()
        {
            return new KeystoreData
            {
                keystoreName = PlayerSettings.Android.keystoreName ?? "",
                keystorePass = PlayerSettings.Android.keystorePass ?? "",
                keyaliasName = PlayerSettings.Android.keyaliasName ?? "",
                keyaliasPass = PlayerSettings.Android.keyaliasPass ?? "",
            };
        }

#if UNITY_6000_0_OR_NEWER
        private Dictionary<string, ProfileOverrideEntry> BuildProfileOverridesMap()
        {
            var map = new Dictionary<string, ProfileOverrideEntry>();

            if (_buildProfiles == null)
                return map;

            foreach (var profile in _buildProfiles)
            {
                string guid = AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(profile));

                string storedJson = LoadRawString(
                    PrefsKeyPrefix + GetProjectKey() + ProfilePrefsKeySuffix + guid);
                KeystoreData stored = string.IsNullOrEmpty(storedJson)
                    ? null : Deserialize(storedJson);

                var current = ReadProfileCurrentSettings(profile);

                bool hasConflict = stored != null && !stored.Equals(current);

                map[guid] = new ProfileOverrideEntry
                {
                    stored = stored,
                    current = current,
                    hasConflict = hasConflict
                };
            }
            return map;
        }

        private KeystoreData ReadProfileCurrentSettings(BuildProfile profile)
        {
            try
            {
                var active = BuildProfile.GetActiveBuildProfile();
                BuildProfile.SetActiveBuildProfile(profile);
                var data = LoadCurrentProjectData();
                if (active != null)
                    BuildProfile.SetActiveBuildProfile(active);
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KeystoreManager] Failed to read profile '" + profile.name +
                    "': " + e.Message);
                return new KeystoreData();
            }
        }
#endif

        private void OnGUI()
        {
            if (_editingData == null)
            {
                _editingData = new KeystoreData();
                _conflictState = ConflictState.NoStoredData;
            }

            EditorGUILayout.Space(10);
            GUILayout.Label("Keystore Helper", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Project", Application.productName);
                EditorGUILayout.TextField("Company", Application.companyName);
            }

            EditorGUILayout.Space(5);
            DrawConflictBanner();
            EditorGUILayout.Space(5);

            _foldoutGlobal = EditorGUILayout.Foldout(_foldoutGlobal, "Keystore Settings", true);
            if (_foldoutGlobal)
            {
                EditorGUI.indentLevel++;
                DrawKeystoreFields(_editingData);
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);
                DrawActionButtons();
            }

#if UNITY_6000_0_OR_NEWER
            if (_buildProfiles != null && _buildProfiles.Count > 0)
            {
                EditorGUILayout.Space(10);
                _foldoutProfiles = EditorGUILayout.Foldout(_foldoutProfiles,
                    "Build Profile Overrides (" + _buildProfiles.Count + ")", true);

                if (_foldoutProfiles)
                {
                    EditorGUI.indentLevel++;
                    foreach (var profile in _buildProfiles)
                        DrawProfileOverrideSection(profile);
                    EditorGUI.indentLevel--;
                }
            }
#endif
        }

        private void DrawConflictBanner()
        {
            switch (_conflictState)
            {
                case ConflictState.NoStoredData:
                    EditorGUILayout.HelpBox(
                        "No keystore data stored in Editor Prefs. " +
                        "Configure fields below and click Save to store.",
                        MessageType.Warning);
                    break;

                case ConflictState.Synced:
                    EditorGUILayout.HelpBox(
                        "Stored data matches project settings.",
                        MessageType.Info);
                    break;

                case ConflictState.Conflict:
                    EditorGUILayout.HelpBox(
                        "Stored data differs from current project settings!",
                        MessageType.Warning);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Apply Stored to Project", GUILayout.Height(25)))
                    {
                        ApplyToProjectSettings(_storedData);
                        _editingData = _storedData.Clone();
                        _conflictState = ConflictState.Synced;
                        Debug.Log("[KeystoreManager] Stored data applied to project settings.");
                        Repaint();
                    }
                    if (GUILayout.Button("Update Stored from Project", GUILayout.Height(25)))
                    {
                        SaveToPrefs(LoadCurrentProjectData());
                        _storedData = LoadCurrentProjectData();
                        _editingData = _storedData.Clone();
                        _conflictState = ConflictState.Synced;
                        Debug.Log("[KeystoreManager] Stored data updated from project settings.");
                        Repaint();
                    }
                    EditorGUILayout.EndHorizontal();
                    break;
            }
        }

        private void DrawKeystoreFields(KeystoreData data)
        {
            EditorGUILayout.LabelField("Keystore File", data.keystoreName);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string newPath = EditorGUILayout.TextField(data.keystoreName);
            if (EditorGUI.EndChangeCheck())
                data.keystoreName = newPath;
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFilePanel(
                    "Select Keystore File", "", "keystore");
                if (!string.IsNullOrEmpty(selected))
                    data.keystoreName = selected;
            }
            EditorGUILayout.EndHorizontal();

            data.keystorePass = DrawPasswordField("Keystore Password", data.keystorePass);
            data.keyaliasName = EditorGUILayout.TextField("Key Alias", data.keyaliasName);
            data.keyaliasPass = DrawPasswordField("Key Password", data.keyaliasPass);
        }

        private string DrawPasswordField(string label, string value)
        {
            return EditorGUILayout.PasswordField(label, value);
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Save to Editor Prefs + Project", GUILayout.Width(210)))
            {
                SaveToPrefs(_editingData);
                ApplyToProjectSettings(_editingData);
                _storedData = _editingData.Clone();
                _conflictState = ConflictState.Synced;
                Debug.Log("[KeystoreManager] Keystore saved to Editor Prefs and applied to project.");
                Repaint();
            }

            bool hasStored = _storedData != null;
            using (new EditorGUI.DisabledGroupScope(!hasStored))
            {
                if (GUILayout.Button("Clear Stored", GUILayout.Width(80)))
                {
                    if (EditorUtility.DisplayDialog("Clear Keystore",
                        "Delete all stored keystore data from Editor Prefs?", "Yes", "No"))
                    {
                        EditorPrefs.DeleteKey(PrefsKeyPrefix + GetProjectKey());
#if UNITY_6000_0_OR_NEWER
                        if (_buildProfiles != null)
                        {
                            foreach (var p in _buildProfiles)
                            {
                                string guid = AssetDatabase.AssetPathToGUID(
                                    AssetDatabase.GetAssetPath(p));
                                EditorPrefs.DeleteKey(
                                    PrefsKeyPrefix + GetProjectKey() + ProfilePrefsKeySuffix + guid);
                            }
                        }
#endif
                        _storedData = null;
                        _editingData = LoadCurrentProjectData().Clone();
                        _conflictState = ConflictState.NoStoredData;
                        _profileOverrides = null;
                        Debug.Log("[KeystoreManager] All stored data cleared.");
                        Repaint();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

#if UNITY_6000_0_OR_NEWER
        private void DrawProfileOverrideSection(BuildProfile profile)
        {
            string profileGuid = AssetDatabase.AssetPathToGUID(
                AssetDatabase.GetAssetPath(profile));

            _profileOverrides.TryGetValue(profileGuid, out var entry);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool hasStored = entry.stored != null;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(profile.name, EditorStyles.boldLabel);
            if (hasStored && entry.hasConflict)
                EditorGUILayout.LabelField("(Conflict)", EditorStyles.miniLabel);
            else if (hasStored)
                EditorGUILayout.LabelField("(Stored)", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField("(No Override)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (hasStored && entry.hasConflict)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply Stored to Profile"))
                {
                    ApplyToProfileSettings(profile, entry.stored);
                    RefreshData();
                    Repaint();
                }
                if (GUILayout.Button("Update Stored from Profile"))
                {
                    SaveProfileToPrefs(profileGuid, ReadProfileCurrentSettings(profile));
                    RefreshData();
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy from Editing"))
            {
                var data = _editingData.Clone();
                SaveProfileToPrefs(profileGuid, data);
                ApplyToProfileSettings(profile, data);
                RefreshData();
                Repaint();
            }
            if (hasStored && GUILayout.Button("Clear Override"))
            {
                EditorPrefs.DeleteKey(
                    PrefsKeyPrefix + GetProjectKey() + ProfilePrefsKeySuffix + profileGuid);
                RefreshData();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void SaveProfileToPrefs(string profileGuid, KeystoreData data)
        {
            string json = Serialize(data);
            string encrypted = Encrypt(json, GetProjectKey());
            EditorPrefs.SetString(
                PrefsKeyPrefix + GetProjectKey() + ProfilePrefsKeySuffix + profileGuid, encrypted);
            Debug.Log("[KeystoreManager] Build profile override saved.");
        }

        private void ApplyToProfileSettings(BuildProfile profile, KeystoreData data)
        {
            var active = BuildProfile.GetActiveBuildProfile();
            BuildProfile.SetActiveBuildProfile(profile);
            ApplyToProjectSettings(data);
            if (active != null)
                BuildProfile.SetActiveBuildProfile(active);
            Debug.Log("[KeystoreManager] Keystore applied to profile: " + profile.name);
        }
#endif

        private void SaveToPrefs(KeystoreData data)
        {
            string json = Serialize(data);
            string encrypted = Encrypt(json, GetProjectKey());
            EditorPrefs.SetString(PrefsKeyPrefix + GetProjectKey(), encrypted);
        }

        private static string LoadRawString(string key)
        {
            string encrypted = EditorPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(encrypted))
                return "";
            return Decrypt(encrypted, GetProjectKey());
        }

        private static string GetProjectKey()
        {
            return Application.companyName + "_" + Application.productName;
        }

        private static string Serialize(KeystoreData data)
        {
            return JsonUtility.ToJson(data);
        }

        private static KeystoreData Deserialize(string json)
        {
            try { return JsonUtility.FromJson<KeystoreData>(json); }
            catch { return null; }
        }

        private static string Encrypt(string plainText, string key)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] result = new byte[plainBytes.Length];
            for (int i = 0; i < plainBytes.Length; i++)
                result[i] = (byte)(plainBytes[i] ^ keyBytes[i % keyBytes.Length]);
            return Convert.ToBase64String(result);
        }

        private static string Decrypt(string cipherText, string key)
        {
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                byte[] result = new byte[cipherBytes.Length];
                for (int i = 0; i < cipherBytes.Length; i++)
                    result[i] = (byte)(cipherBytes[i] ^ keyBytes[i % keyBytes.Length]);
                return Encoding.UTF8.GetString(result);
            }
            catch { return ""; }
        }
    }

    [Serializable]
    internal class KeystoreData
    {
        public string keystoreName = "";
        public string keystorePass = "";
        public string keyaliasName = "";
        public string keyaliasPass = "";

        public KeystoreData Clone()
        {
            return new KeystoreData
            {
                keystoreName = keystoreName,
                keystorePass = keystorePass,
                keyaliasName = keyaliasName,
                keyaliasPass = keyaliasPass,
            };
        }

        public bool Equals(KeystoreData other)
        {
            if (other == null) return false;
            return keystoreName == other.keystoreName
                && keystorePass == other.keystorePass
                && keyaliasName == other.keyaliasName
                && keyaliasPass == other.keyaliasPass;
        }
    }
}
