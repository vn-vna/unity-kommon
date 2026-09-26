using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Com.Scheherazade.Common.VIC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [AddComponentMenu("Scheherazade/Version Info/Version Info Canvas")]
    public class VersionInfoCanvas : MonoBehaviour
    {
        #region Public Methods
        public void SetVersionInfo(string version)
        {
            Text[] legacyTexts = GetComponentsInChildren<Text>(true);
            foreach (Text text in legacyTexts)
            {
                text.text = version;
            }

            TMP_Text[] tmpTexts = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in tmpTexts)
            {
                text.text = version;
            }
        }
        #endregion
    }
}
