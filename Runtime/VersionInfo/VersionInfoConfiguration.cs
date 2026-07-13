using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC
{
    [CreateAssetMenu(
        fileName = "VersionInfoConfiguration",
        menuName = "Scheherazade/Version Info/Configuration"
    )]
    public class VersionInfoConfiguration : ScriptableObject
    {
        #region Serialized Fields
        [SerializeField]
        [Tooltip(
            "Template for the version string. Use {placeholders} resolved at build time.\n"
            + "Available: {app-version}, {app-name}, {app-bundle}, {build-type}, "
            + "{date}, {time}, {datetime}, {project-name},\n"
            + "{git-branch}, {git-commit}, {git-commit-full}, {platform}\n"
            + "Custom format: {datetime:yyMMdd-HHmm}, {date:yyMMdd}, {time:HHmm}\n"
            + "NoBuild flags: {flag-debug}, {flag-cheat}")]
        private string _versionPattern = "v{app-version}_{build-type}_{datetime}";

        [SerializeField]
        private ScriptableObject _provider;

        [SerializeField]
        private List<ScriptableObject> _consumers;

        [SerializeField]
        [Tooltip("Custom placeholder providers that can resolve additional "
            + "{placeholders} or override built-in ones.")]
        private List<ScriptableObject> _placeholderProviders;
        #endregion

        #region Interfaces & Properties
        public string VersionPattern => _versionPattern;

        public IVersionInfoProvider Provider => _provider as IVersionInfoProvider;

        public List<ScriptableObject> ConsumerAssets => _consumers;

        public List<ScriptableObject> PlaceholderProviderAssets => _placeholderProviders;
        #endregion
    }
}
