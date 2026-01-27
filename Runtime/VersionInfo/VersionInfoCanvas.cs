using System.Linq;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.VIC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public abstract class VersionInfoCanvas<T> :
        SingletonBehavior<T>
        where T : VersionInfoCanvas<T>
    {
        [SerializeField]
        [HideInInspector]
        private Canvas kanvas;

        [SerializeField]
        private VersionInfoDefinition versionInfoDefinition;

#if UNITY_EDITOR
        private void OnValidate()
        {
            kanvas = GetComponent<Canvas>();
        }
#endif

        private void Start()
        {
            if (versionInfoDefinition != null)
            {
                SetVersionInfo(versionInfoDefinition.VersionTag);
            }
        }

        protected abstract void SetVersionInfo(string vi);
    }
}