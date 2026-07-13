using UnityEngine;
using UnityEngine.UI;

namespace Com.Hapiga.Scheherazade.Common.VIC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [AddComponentMenu("Scheherazade/Version Info/Version Info Canvas")]
    public class VersionInfoCanvas : MonoBehaviour
    {
        #region Public Methods
        public void SetVersionInfo(string version)
        {
            Text[] texts = GetComponentsInChildren<Text>(true);
            foreach (Text text in texts)
            {
                text.text = version;
            }
        }
        #endregion
    }
}
