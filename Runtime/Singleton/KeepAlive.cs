using UnityEngine;

namespace Com.Scheherazade.Common.Singleton
{
    [AddComponentMenu("Scheherazade/Common/Dont Destroy On Load")]
    public class KeepAliveComponent : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }
    }
}