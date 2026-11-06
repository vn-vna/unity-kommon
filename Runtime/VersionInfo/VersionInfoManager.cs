using Com.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Scheherazade.Common.VIC
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
            if (Object.FindAnyObjectByType<VersionInfoManager>(
                    FindObjectsInactive.Include) != null)
            {
                return;
            }

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
            if (_config?.Provider == null)
            {
                return $"v{Application.version}";
            }

            try
            {
                return _config.Provider.GetVersionInfo();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                return $"v{Application.version}";
            }
        }

        private void DispatchToConsumers(string version)
        {
            if (_config == null || _config.ConsumerAssets == null)
            {
                return;
            }

            foreach (ScriptableObject asset in _config.ConsumerAssets)
            {
                if (asset is not IVersionInfoConsumer consumer)
                {
                    continue;
                }

                try
                {
                    if (consumer.IsActive)
                    {
                        consumer.Consume(version);
                    }
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(
                        $"[VersionInfo] Consumer "
                        + $"'{asset.name}' failed.");
                    Debug.LogException(exception);
                }
            }
        }
        #endregion
    }
}
