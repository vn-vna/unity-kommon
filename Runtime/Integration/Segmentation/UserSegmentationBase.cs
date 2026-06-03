using System;
using System.Collections;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.LocalSave;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using Com.Hapiga.Scheherazade.Common.Threading;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Segmentation
{
    public interface IUserSegmentationTracker
    {
        public void SegmentationDataUpdated(SegmentationInformation info, SegmentationDeclaration declaration);
    }

    public abstract class UserSegmentationBase<T> :
        SingletonScriptableObject<T>,
        IUserSegmentation,
        IIntegrationModule
        where T : ScriptableObject
    {
        #region Constants
        private const string SegmentationSaveKey = "__segment_data__";
        #endregion

        #region Interfaces & Properties
        public UserSegmentationStatus Status { get; private set; } = UserSegmentationStatus.Uninitialized;
        public bool IsFirstSegmentDetermined => _firstSegmentDetermined;
        public SegmentationInformation SegmentInformation => _userSegmentation;
        public SegmentationDeclaration CurrentSegmentDeclaration => _currentSegmentDeclaration;
        #endregion

        #region Serialized Fields
        [SerializeField]
        private UserSegmentationConfiguration configuration;
        #endregion

        #region Private Fields
        private bool _firstSegmentDetermined = false;
        private SegmentationInformation _userSegmentation;
        private SegmentationDeclaration _currentSegmentDeclaration;
        private List<IUserSegmentationTracker> _segmentationTrackers;
        #endregion

        #region Unity Methods
        protected override void OnEnable()
        {
            base.OnEnable();

            _segmentationTrackers ??= new List<IUserSegmentationTracker>();
            Integration.RegisterManager(this);
        }

        public virtual void Reset()
        {
            Status = UserSegmentationStatus.Uninitialized;
            _segmentationTrackers ??= new List<IUserSegmentationTracker>();
            _segmentationTrackers.Clear();
            _firstSegmentDetermined = false;
            _userSegmentation = null;
            _currentSegmentDeclaration = null;
        }
        #endregion

        #region Public Methods
        public void Initialize()
        {
            Dispatcher.DispatchCoroutine(InitializeCoroutine());
        }

        public IEnumerator InitializeCoroutine()
        {
            if (Status == UserSegmentationStatus.Initialized)
            {
                yield break;
            }

            Status = UserSegmentationStatus.Initializing;

            if (!LocalFileHandler.Exists(SegmentationSaveKey))
            {
                QuickLog.Info<UserSegmentationBase<T>>(
                    "No saved segmentation data found."
                );
                Status = UserSegmentationStatus.Initialized;
                yield break;
            }

            _userSegmentation = LocalFileHandler.Load<SegmentationInformation>(SegmentationSaveKey);
            DetermineUserSegmention(_userSegmentation);
            SegmentationDataUpdated();
            _firstSegmentDetermined = true;
            Status = UserSegmentationStatus.Initialized;
            yield return null;
        }

        public void RegisterSegmentationTracker(IUserSegmentationTracker tracker)
        {
            if (_segmentationTrackers.Contains(tracker)) return;
            _segmentationTrackers.Add(tracker);
        }

        public void NotifySegmentationTrackers()
        {
            Dispatcher.DispatchCoroutine(NotifySegmentationTrackersCoroutine());
        }

        public void RegisterSegmentation(SegmentationInformation info)
        {
            QuickLog.Info<UserSegmentationBase<T>>(
                "Registering user segmentation data."
            );

            _userSegmentation = info;
            DetermineUserSegmention(info);
            SegmentationDataUpdated();
            _firstSegmentDetermined = true;
            Status = UserSegmentationStatus.Initialized;

            LocalFileHandler.Save(info, SegmentationSaveKey);

            QuickLog.Info<UserSegmentationBase<T>>(
                "User segmentation data registered and saved."
            );
        }
        #endregion

        #region Private Methods
        private IEnumerator NotifySegmentationTrackersCoroutine()
        {
            while (!_firstSegmentDetermined)
            {
                yield return null;
            }

            QuickLog.Info<UserSegmentationBase<T>>(
                "Notifying {0} segmentation trackers.",
                _segmentationTrackers.Count
            );

            foreach (IUserSegmentationTracker tracker in _segmentationTrackers)
            {
                tracker.SegmentationDataUpdated(_userSegmentation, _currentSegmentDeclaration);
            }
        }

        private void DetermineUserSegmention(SegmentationInformation info)
        {
            SegmentationDeclaration matched = null;
            foreach (SegmentationDeclaration declaration in configuration.Declarations)
            {
                if (!declaration.Matches(info)) continue;
                matched = declaration;
                break;
            }

            if (matched == null)
            {
                QuickLog.Warning<UserSegmentationBase<T>>(
                    "User did not match any segmentation declaration."
                );
                return;
            }

            _currentSegmentDeclaration = matched;
            QuickLog.Info<UserSegmentationBase<T>>(
                "User matched segmentation: {0}",
                matched.SegmentName
            );
        }

        protected virtual void SegmentationDataUpdated()
        {
            QuickLog.Info<UserSegmentationBase<T>>(
                "Notifying {0} segmentation trackers about segmentation data update.",
                _segmentationTrackers.Count
            );
        }
        #endregion

    }
}