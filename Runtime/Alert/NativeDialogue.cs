using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Alert
{

    public class NativeDialogue : MonoBehaviour
    {
        private static NativeDialogue instance;

        // Callback events
        public System.Action OnPositiveButtonClicked;
        public System.Action OnNegativeButtonClicked;

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Shows a simple alert dialog with just a title, message, and OK button
        /// </summary>
        public static void ShowSimpleAlert(string title, string message)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass nativeDialog = new AndroidJavaClass("com.hapiga.scheherazade.android.NativeDialog"))
            {
                nativeDialog.CallStatic("showSimpleAlert", title, message);
            }
        }
        catch (System.Exception e)
        {
            Debug. LogError("Error showing native dialog: " + e.Message);
        }
#else
            Debug.Log($"[Native Dialog] Title: {title}, Message: {message}");
#endif
        }

        /// <summary>
        /// Shows an alert dialog with custom buttons and callbacks
        /// </summary>
        public static void ShowAlert(string title, string message, string positiveButton, string negativeButton, System.Action onPositive = null, System.Action onNegative = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            // Ensure instance exists for callbacks
            if (instance == null)
            {
                GameObject go = new GameObject("AndroidNativeDialog");
                instance = go. AddComponent<NativeDialogue>();
            }

            instance.OnPositiveButtonClicked = onPositive;
            instance. OnNegativeButtonClicked = onNegative;

            using (AndroidJavaClass nativeDialog = new AndroidJavaClass("com.hapiga.scheherazade.android.NativeDialog"))
            {
                nativeDialog.CallStatic("showAlert", title, message, positiveButton, negativeButton, "AndroidNativeDialog", "OnDialogCallback");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error showing native dialog: " + e.Message);
        }
#else
            Debug.Log($"[Native Dialog] Title: {title}, Message: {message}, Positive: {positiveButton}, Negative: {negativeButton}");
            if (onPositive != null)
            {
                Debug.Log("(Simulating positive button click in editor)");
            }
#endif
        }

        // This method is called from Java via UnitySendMessage
        void OnDialogCallback(string buttonType)
        {
            Debug.Log("Dialog callback received: " + buttonType);

            if (buttonType == "positive" && OnPositiveButtonClicked != null)
            {
                OnPositiveButtonClicked.Invoke();
                OnPositiveButtonClicked = null;
            }
            else if (buttonType == "negative" && OnNegativeButtonClicked != null)
            {
                OnNegativeButtonClicked.Invoke();
                OnNegativeButtonClicked = null;
            }
        }
    }

}