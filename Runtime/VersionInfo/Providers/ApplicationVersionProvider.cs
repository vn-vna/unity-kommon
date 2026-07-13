using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC.Providers
{
    [CreateAssetMenu(
        fileName = "ApplicationVersionProvider",
        menuName = "Scheherazade/Version Info/Application Version Provider"
    )]
    public class ApplicationVersionProvider : ScriptableObject, IVersionInfoProvider
    {
        #region IVersionInfoProvider
        public string GetVersionInfo()
        {
            return $"v{Application.version}";
        }
        #endregion
    }
}
