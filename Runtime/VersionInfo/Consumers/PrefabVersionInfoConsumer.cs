using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.VIC.Consumers
{
    [CreateAssetMenu(
        fileName = "PrefabVersionInfoConsumer",
        menuName = "Scheherazade/Version Info/Prefab Version Info Consumer"
    )]
    public class PrefabVersionInfoConsumer : ScriptableObject, IVersionInfoConsumer
    {
        #region Serialized Fields
        [SerializeField]
        private GameObject _prefab;
        #endregion

        #region Private Fields
        private GameObject _instance;
        #endregion

        #region IVersionInfoConsumer
        public bool IsActive => _prefab != null;

        public void Consume(string versionInfo)
        {
            if (_instance == null && _prefab != null)
            {
                _instance = Object.Instantiate(_prefab);
                Object.DontDestroyOnLoad(_instance);
            }

            if (_instance == null)
            {
                return;
            }

            VersionInfoCanvas canvas = _instance.GetComponent<VersionInfoCanvas>();
            if (canvas != null)
            {
                canvas.SetVersionInfo(versionInfo);
            }
        }
        #endregion
    }
}
