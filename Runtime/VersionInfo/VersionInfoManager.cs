using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC
{
    [AddComponentMenu("")]
    public class VersionInfoManager : MonoBehaviour
    {
        #region Constants
        private const string ConfigResourcePath = "VersionInfoConfiguration";
        #endregion

        #region Private Fields
        private VersionInfoConfiguration _config;
        #endregion

        #region Bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameObject go = new GameObject("[Scheherazade Version Info]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<KeepAliveComponent>();
            go.AddComponent<VersionInfoManager>();
        }
        #endregion

        #region Unity Callbacks
        private void Awake()
        {
            _config = Resources.Load<VersionInfoConfiguration>(ConfigResourcePath);
        }

        private void Start()
        {
            string version = ResolveVersion();
            DispatchToConsumers(version);
        }
        #endregion

        #region Private Methods
        private string ResolveVersion()
        {
            if (_config != null && _config.Provider != null)
            {
                return _config.Provider.GetVersionInfo();
            }

            return $"v{Application.version}";
        }

        private void DispatchToConsumers(string version)
        {
            if (_config == null || _config.ConsumerAssets == null)
            {
                return;
            }

            foreach (ScriptableObject asset in _config.ConsumerAssets)
            {
                if (asset is IVersionInfoConsumer consumer && consumer.IsActive)
                {
                    consumer.Consume(version);
                }
            }
        }
        #endregion
    }
}
