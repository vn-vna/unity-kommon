using System.IO;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Archetype.Editor
{
    public sealed class ArchetypeSettings : ScriptableObject
    {
        public const string ResourcePath = "Scheherazade/ArchetypeSettings";
        public const string AssetFolder = "Assets/Resources/Scheherazade";
        public const string AssetPath = AssetFolder + "/ArchetypeSettings.asset";

        private const string PluginRoot =
            "Assets/Plugins/ScheherazadeCommon/Runtime/Archetype";

        [Header("Generation")]
        [Tooltip(
            "Folder for generated .g.cs files. "
            + "If the path starts with 'Assets/', it is used as-is. "
            + "Otherwise, it is relative to the plugin Archetype folder."
            + "\n\nNote: folders ending with '~' are hidden from "
            + "Unity's asset database and will not be compiled "
            + "by the normal pipeline."
        )]
        public string generatedFolder = "Assets/Generated~";

        [Tooltip(
            "Automatically regenerate archetypes on domain reload "
            + "when the hash changes."
        )]
        public bool autoGenerateOnReload = true;

        [Header("Namespace")]
        [Tooltip(
            "Optional override namespace for generated classes. "
            + "Leave empty to use the Runtime assembly's root namespace."
        )]
        public string namespaceOverride = "";

        [Header("Build")]
        [Tooltip(
            "Optional target folder for compiled managed DLL output. "
            + "Leave empty to skip managed plugin build."
        )]
        public string managedOutputFolder = "";

        public string FullGeneratedFolder =>
            generatedFolder.StartsWith("Assets/")
                ? Path.Combine(
                    Application.dataPath,
                    "..",
                    generatedFolder).Replace('\\', '/')
                : Path.Combine(PluginRoot, generatedFolder)
                    .Replace('\\', '/');
    }
}
