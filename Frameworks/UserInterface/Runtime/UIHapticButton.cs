using System;
using System.Reflection;
using HapticsFacade = Com.Scheherazade.Common.Haptics.Haptics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Com.Scheherazade.Common.UserInterface
{
    [AddComponentMenu("UI/UI Haptic Button")]
    [DisallowMultipleComponent]
    public sealed class UIHapticButton : MonoBehaviour,
        IPointerClickHandler,
        ISubmitHandler
    {
        #region Constants

        public const string DefaultRhythmId = "medium_tap";
        public const string PlayButtonRhythmId = "game_loaded_triple_medium";

        private const string MagimaButtonTypeName = "Magima.UI.UIButton";

        #endregion

        #region Serialized Fields

        [Tooltip("Haptic rhythm played after this button accepts a click.")]
        [SerializeField]
        private string rhythmId = DefaultRhythmId;

        #endregion

        #region Private Fields

        private Selectable _selectable;
        private Component _customButton;
        private PropertyInfo _customInteractableProperty;

        #endregion

        #region Properties

        public string RhythmId => rhythmId;

        #endregion

        #region Unity Callbacks

        private void Awake()
        {
            CacheButtonState();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(rhythmId))
            {
                rhythmId = DefaultRhythmId;
            }
        }
#endif

        #endregion

        #region Public Methods

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_customButton == null && _selectable == null)
            {
                CacheButtonState();
            }

            if (_customButton != null)
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                PlayHaptic();
            }
        }

        public void OnSubmit(BaseEventData eventData)
        {
            PlayHaptic();
        }

        public void PlayHaptic()
        {
            PlayIfInteractable();
        }

        public void SetRhythm(string customRhythmId)
        {
            rhythmId = string.IsNullOrWhiteSpace(customRhythmId)
                ? DefaultRhythmId
                : customRhythmId;
        }

        public static int EnsureForHierarchy(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            int addedCount = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                GameObject target = transforms[index].gameObject;
                if (!IsButton(target))
                {
                    continue;
                }

                UIHapticButton haptic = target.GetComponent<UIHapticButton>();
                if (haptic == null)
                {
                    haptic = target.AddComponent<UIHapticButton>();
                    addedCount++;
                }

                haptic.SetRhythm(IsPlayButton(target.name)
                    ? PlayButtonRhythmId
                    : DefaultRhythmId);
            }

            return addedCount;
        }

        #endregion

        #region Private Methods

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeRuntimeCoverage()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                EnsureScene(SceneManager.GetSceneAt(index));
            }
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureScene(scene);
        }

        private static void EnsureScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                EnsureForHierarchy(roots[index]);
            }
        }

        private static bool IsButton(GameObject target)
        {
            if (target.GetComponent<Button>() != null)
            {
                return true;
            }

            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (behaviour != null
                    && behaviour.GetType().FullName == MagimaButtonTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPlayButton(string objectName)
        {
            return !string.IsNullOrEmpty(objectName)
                && objectName.IndexOf(
                    "play",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0
                && objectName.IndexOf(
                    "replay",
                    StringComparison.OrdinalIgnoreCase
                ) < 0;
        }

        private void CacheButtonState()
        {
            _selectable = GetComponent<Selectable>();
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (behaviour == null
                    || behaviour.GetType().FullName != MagimaButtonTypeName)
                {
                    continue;
                }

                _customButton = behaviour;
                _customInteractableProperty = behaviour.GetType().GetProperty(
                    "interactable",
                    BindingFlags.Instance | BindingFlags.Public
                );
                return;
            }
        }

        private void PlayIfInteractable()
        {
            if (!IsInteractable())
            {
                return;
            }

            HapticsFacade.PlayRhythm(rhythmId);
        }

        private bool IsInteractable()
        {
            if (_selectable != null)
            {
                return _selectable.IsInteractable();
            }

            if (_customButton == null)
            {
                CacheButtonState();
            }

            if (_customButton == null || _customInteractableProperty == null)
            {
                return true;
            }

            return _customInteractableProperty.GetValue(_customButton) is not bool value
                || value;
        }

        #endregion
    }
}
