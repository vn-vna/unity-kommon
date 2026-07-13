using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC.Providers
{
    [CreateAssetMenu(
        fileName = "ResourceTextAssetProvider",
        menuName = "Scheherazade/Version Info/Resource Text Asset Provider"
    )]
    public class ResourceTextAssetProvider : ScriptableObject, IVersionInfoProvider
    {
        #region Serialized Fields
        [SerializeField]
        private string _resourceFileName = "VersionInfo";
        #endregion

        #region Interfaces & Properties
        public string ResourceFileName => _resourceFileName;
        #endregion

        #region IVersionInfoProvider
        public string GetVersionInfo()
        {
            TextAsset asset = Resources.Load<TextAsset>(_resourceFileName);
            if (asset != null)
            {
                return asset.text;
            }

            return $"v{Application.version}";
        }
        #endregion
    }
}
