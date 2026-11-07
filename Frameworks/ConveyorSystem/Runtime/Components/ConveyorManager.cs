using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Scheherazade/Conveyor System/Conveyor Manager")]
    public sealed class ConveyorManager : MonoBehaviour
    {
        #region Events & Delegates
        public event Action<ConveyorResult> ResultPublished;
        #endregion

        #region Properties
        public ConveyorWorld World { get; private set; }
        public bool Running { get; set; } = true;
        public double TimeDebt => _accumulator;
        public Exception LastError { get; private set; }
        #endregion

        #region Serialized Fields
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private Conveyor[] conveyors = Array.Empty<Conveyor>();
        [SerializeField, Min(1)] private int payloadCapacity = 4096;
        [SerializeField, Min(0.001f)] private float fixedStep = 1f / 60f;
        [SerializeField, Range(1, 64)] private int maximumCatchUpSteps = 8;
        #endregion

        #region Private Fields
        private double _accumulator;
        private Action<ConveyorResult> _publishResult;
        #endregion

        #region Unity Callbacks
        private void Start() { if (initializeOnStart && World == null) Initialize(); }

        private void Update()
        {
            if (!Running || World == null || World.IsDisposed || LastError != null) return;
            _accumulator += Time.deltaTime;
            try
            {
                int steps = 0;
                while (_accumulator >= fixedStep && steps++ < maximumCatchUpSteps)
                {
                    Advance();
                    _accumulator -= fixedStep;
                }
            }
            catch (Exception error) { LastError = error; Running = false; throw; }
        }

        private void LateUpdate()
        {
            if (World == null || World.IsDisposed || LastError != null) return;
            for (int index = 0; index < conveyors.Length; index++)
            {
                if (conveyors[index] != null) conveyors[index].Render();
            }
        }

        private void OnDestroy() { Shutdown(ConveyorShutdownDisposition.Discard); }
        #endregion

        #region Public Methods
        public void Configure(Conveyor[] owners, int capacity = 4096, float step = 1f / 60f)
        {
            if (World != null) throw new InvalidOperationException("Shut down the manager before reconfiguration.");
            if (owners == null || capacity < 1 || capacity > 1000000) throw new ArgumentOutOfRangeException(nameof(capacity));
            ConveyorValidation.Positive(step, nameof(step));
            conveyors = (Conveyor[])owners.Clone();
            payloadCapacity = capacity;
            fixedStep = step;
        }

        public void Initialize()
        {
            if (World != null) throw new InvalidOperationException("Manager is already initialized.");
            if (payloadCapacity < 1 || payloadCapacity > 1000000 || maximumCatchUpSteps < 1)
                throw new ArgumentOutOfRangeException(nameof(payloadCapacity));
            ConveyorValidation.Positive(fixedStep, nameof(fixedStep));
            var world = new ConveyorWorld(payloadCapacity, checked(payloadCapacity * 2 + 128));
            _publishResult = HandleResult;
            world.ClaimClock(this);
            int initialized = 0;
            try
            {
                for (int index = 0; index < conveyors.Length; index++)
                {
                    if (conveyors[index] == null) throw new ArgumentException("The manager has an empty conveyor slot.");
                    conveyors[index].Initialize(world);
                    initialized++;
                    if (conveyors[index].Runtime.Backend.StepDriver != ConveyorStepDriver.Synchronous)
                        throw new NotSupportedException("ConveyorManager drives synchronous backends only. Use a world with an explicit external driver for physics.");
                }
                World = world;
                LastError = null;
                _accumulator = 0;
            }
            catch
            {
                for (int index = 0; index < initialized; index++) conveyors[index].Shutdown();
                world.Dispose();
                throw;
            }
        }

        public void StepOnce()
        {
            if (Running) throw new InvalidOperationException("Pause the manager before manual stepping.");
            if (World == null) throw new InvalidOperationException("Initialize the manager first.");
            Advance();
        }

        public void Shutdown(ConveyorShutdownDisposition disposition = ConveyorShutdownDisposition.RequireEmpty)
        {
            if (World == null) return;
            World.DispatchResults(_publishResult);
            World.Shutdown(disposition);
            World.DispatchResults(_publishResult);
            for (int index = 0; index < conveyors.Length; index++)
            {
                if (conveyors[index] != null) conveyors[index].Shutdown(disposition);
            }
            World = null;
        }
        #endregion

        #region Private Methods
        private void Advance()
        {
            World.DispatchResults(_publishResult);
            World.Tick(fixedStep, this);
            World.DispatchResults(_publishResult);
        }

        private void HandleResult(ConveyorResult result) { ResultPublished?.Invoke(result); }
        #endregion
    }
}
