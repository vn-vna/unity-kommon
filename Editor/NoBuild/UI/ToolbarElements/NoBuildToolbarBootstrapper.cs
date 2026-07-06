// ═══════════════════════════════════════════════════════════
// ── NoBuildToolbarBootstrapper ────────────────────────
// ═══════════════════════════════════════════════════════════

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Com.Hapiga.Scheherazade.Common.NoBuild.Editor
{
    /// <summary>
    /// Injects NoBuild toolbar elements into the Unity 6 main toolbar
    /// by finding the <c>UnityEditor.Toolbar</c> instance and adding
    /// <see cref="VisualElement"/>s to the left and right toolbar zones.
    /// </summary>
    [InitializeOnLoad]
    internal static class NoBuildToolbarBootstrapper
    {
        private const int MaxAttempts = 300;
        private const string LeftZoneName = "ToolbarZoneLeftAlign";
        private const string RightZoneName = "ToolbarZoneRightAlign";

        private static int _attempts;
        private static bool _initialized;

        static NoBuildToolbarBootstrapper()
        {
            EditorApplication.update -= TryInitialize;
            EditorApplication.update += TryInitialize;
        }

        private static void TryInitialize()
        {
            if (_initialized) return;
            _attempts++;

            try
            {
                Type toolbarType = typeof(UnityEditor.Editor).Assembly.GetType(
                    "UnityEditor.Toolbar");
                if (toolbarType == null)
                {
                    Fail("UnityEditor.Toolbar type not found.");
                    return;
                }

                UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
                if (toolbars == null || toolbars.Length == 0)
                {
                    if (_attempts < MaxAttempts) return;
                    Fail("UnityEditor.Toolbar instance not found.");
                    return;
                }

                FieldInfo rootField = toolbarType.GetField(
                    "m_Root",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);

                if (rootField == null)
                {
                    Fail("m_Root field not found on Toolbar.");
                    return;
                }

                VisualElement root = rootField.GetValue(toolbars[0]) as VisualElement;
                if (root == null)
                {
                    Fail("m_Root is null.");
                    return;
                }

                // Find the left and right zones by name
                VisualElement leftZone = FindZone(root, LeftZoneName);
                VisualElement rightZone = FindZone(root, RightZoneName);

                if (leftZone == null && rightZone == null)
                {
                    if (_attempts < MaxAttempts) return;
                    Fail("Toolbar zones not found.");
                    return;
                }

                // ── Inject LEFT zone: Scene Set element ──
                if (leftZone != null)
                {
                    VisualElement sceneSetEl = CreateToolbarElement<NoBuildSceneSetToolbarElement>();
                    leftZone.Add(sceneSetEl);
                }

                // ── Inject RIGHT zone: Define Set + Build elements ──
                if (rightZone != null)
                {
                    VisualElement defineEl = CreateToolbarElement<NoBuildDefineSetToolbarElement>();
                    rightZone.Insert(0, defineEl);

                    VisualElement buildEl = CreateToolbarElement<NoBuildBuildToolbarElement>();
                    rightZone.Insert(1, buildEl);
                }

                _initialized = true;
                EditorApplication.update -= TryInitialize;
                Debug.Log("[NoBuild] Toolbar injection complete.");
            }
            catch (Exception ex)
            {
                if (_attempts >= MaxAttempts)
                    Fail($"Injection exception: {ex.Message}");
            }
        }

        private static VisualElement FindZone(VisualElement root, string name)
        {
            if (root.name == name) return root;
            foreach (VisualElement child in root.Children())
            {
                VisualElement found = FindZone(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static VisualElement CreateToolbarElement<T>() where T : VisualElement, new()
        {
            T element = new T();
            element.style.flexShrink = 0;
            element.style.flexGrow = 0;
            return element;
        }

        private static void Fail(string reason)
        {
            Debug.LogWarning($"[NoBuild] {reason} Toolbar buttons disabled.");
            EditorApplication.update -= TryInitialize;
        }
    }
}
