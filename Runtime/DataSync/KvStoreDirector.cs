using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public class KvStoreDirector : MonoBehaviour
    {
        #region Events & Delegates

        public static event System.Action Paused;

        public static event System.Action Quitting;

        #endregion

        #region Unity Callbacks

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus) return;

            Paused?.Invoke();

            _ = KvStore.ForceSyncAllAsync();
        }

        private void OnApplicationQuit()
        {
            Quitting?.Invoke();

            _ = KvStore.ForceSyncAllAsync();
        }

        #endregion
    }
}
