using System;
using System.Collections;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.IAR
{
    public abstract class InAppReviewManagerBase<T> :
        SingletonBehavior<T>,
        IInAppReviewManager
        where T : SingletonBehavior<T>
    {
        public IInAppReviewModule Module { get; private set; }
        public InAppReviewManagerStatus Status { get; private set; }

        private float _initTimer;

        protected override void Awake()
        {
            base.Awake();

            Status = InAppReviewManagerStatus.Uninitialized;
            Integration.RegisterManager(this);
        }

        public void Initialize()
        {
            StartCoroutine(InitializeCoroutine());
        }

        public void RegisterModule(IInAppReviewModule module)
        {
            Module = module;
        }

        public IEnumerator InitializeCoroutine()
        {
            if (Module == null)
            {
                yield break;
            }

            if (Module.IsInitialized)
            {
                yield break;
            }

            Status = InAppReviewManagerStatus.Initializing;
            Module.Initialize();

            _initTimer = 0.0f;

            while (_initTimer < 2.0f && !Module.IsInitialized)
            {
                _initTimer += Time.deltaTime;
                yield return null;
            }

            if (Module.IsInitialized)
            {
                Status = InAppReviewManagerStatus.Ready;
            }
            else
            {
                Status = InAppReviewManagerStatus.Uninitialized;
            }

        }

        public void PerformInAppReviewRequest()
        {
            if (Module == null)
            {
                Debug.LogWarning("In-App Review Module is not set.");
                return;
            }

            if (Status != InAppReviewManagerStatus.Ready)
            {
                Debug.LogWarning("In-App Review Manager is not ready.");
                return;
            }

            StartCoroutine(Module.PerformInAppReviewRequest());
        }

    }
}