using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    [AddComponentMenu("Scheherazade/Puzzle Levels/Preloader Setup")]
    public class PuzzleLevelPreloaderSetup : MonoBehaviour
    {
        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Preloader configuration asset. If null, preloader uses default values.")]
#endif
        [SerializeField]
        private PuzzleLevelPreloaderConfig _config;

        #endregion

        #region Properties

        public PuzzleLevelPreloader Preloader { get; private set; }

        public PuzzleLevelPreloaderConfig Config
        {
            get => _config;
            set
            {
                _config = value;
                if (Preloader != null && value != null)
                {
                    Preloader.ApplyConfig(value);
                }
            }
        }

        #endregion

        #region Unity Callbacks

        private void Awake()
        {
            PuzzleLevelPreloader existing = FindObjectOfType<
                PuzzleLevelPreloader>();
            if (existing != null && existing.gameObject != gameObject)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            gameObject.hideFlags |= HideFlags.HideInHierarchy;

            Preloader = gameObject.AddComponent<PuzzleLevelPreloader>();
            if (_config != null)
            {
                Preloader.ApplyConfig(_config);
            }
        }

        #endregion

        #region Public Methods

        public static PuzzleLevelPreloaderSetup CreateFromConfig(
            PuzzleLevelPreloaderConfig config
        )
        {
            GameObject go = new GameObject("[PuzzleLevelPreloader]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;

            PuzzleLevelPreloaderSetup setup
                = go.AddComponent<PuzzleLevelPreloaderSetup>();
            setup.Config = config;
            return setup;
        }

        #endregion
    }
}
