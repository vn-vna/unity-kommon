using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Tunables for the <see cref="LoadingManager"/>, loaded from
    /// <c>Resources/Integration/Managers/LoadingConfiguration</c>.
    /// </summary>
    [CreateAssetMenu(
        fileName = "LoadingConfiguration",
        menuName = "Scheherazade/Loading/Configuration"
    )]
    public class LoadingConfiguration : ScriptableObject
    {
        #region Serialized Fields
        [SerializeField]
        [Tooltip("Minimum total loading time before the loading screen can close.")]
        private float minimumLoadingTime = 1f;

        [SerializeField]
        [Range(0.001f, 0.5f)]
        [Tooltip("Delta-time MoveTowards speed used to smooth the displayed progress.")]
        private float progressSmoothening = 0.05f;

        [SerializeField]
        [Tooltip("Frames cycled while loading, e.g. \"Loading\", \"Loading.\"...")]
        private string[] loadingTexts =
        {
            "Loading",
            "Loading.",
            "Loading..",
            "Loading...",
        };

        [SerializeField]
        [Tooltip("Seconds between text frame changes.")]
        private float loadingTextInterval = 0.3f;

        [SerializeField]
        [Tooltip("Default execution mode for groups created via the builder when no WithMode(...) is set.")]
        private LoadingExecutionMode defaultExecutionMode = LoadingExecutionMode.Sequential;
        #endregion

        #region Properties
        public float MinimumLoadingTime => minimumLoadingTime;
        public float ProgressSmoothening => progressSmoothening;
        public IReadOnlyList<string> LoadingTexts => loadingTexts;
        public float LoadingTextInterval => loadingTextInterval;
        public LoadingExecutionMode DefaultExecutionMode => defaultExecutionMode;
        #endregion
    }
}
