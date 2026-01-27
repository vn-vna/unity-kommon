using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC
{
    [CreateAssetMenu(
        fileName = "VersionInfoDefinition",
        menuName = "Dev Menu/Version Info/Version Info Definition"
    )]
    public class VersionInfoDefinition : ScriptableObject
    {
        public string VersionTag
        {
            get
            {
#if UNITY_EDITOR
                return $"v{Application.version}_editor";
#else   
                return $"v{Application.version}_{buildName}";
#endif
            }
        }

        [SerializeField]
        protected string buildName;

#if UNITY_EDITOR
        private void OnValidate()
        {
            HandlePreBuildVersionResolve();
        }
#endif

        public virtual void HandlePreBuildVersionResolve()
        {
            string rel = null;
#if PRODUCTION_BUILD
            rel = "prod";
#else
            rel = "dev";
#endif

            buildName = $"{rel}_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}";
        }
    }

#if UNITY_EDITOR
    internal class VersionInfoUpdatePreprocessor :
        UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            VersionInfoDefinition v = UnityEditor.AssetDatabase
                .FindAssets($"t:{nameof(VersionInfoDefinition)}")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<VersionInfoDefinition>)
                .FirstOrDefault(so => so != null);

            if (v == null) return;
            v.HandlePreBuildVersionResolve();
        }
    }
#endif
}