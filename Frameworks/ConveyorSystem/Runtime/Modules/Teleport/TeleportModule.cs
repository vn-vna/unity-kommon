using System;

namespace Com.Scheherazade.Common.Frameworks.ConveyorSystem
{
    public sealed class BlockingTransferPolicy : IConveyorTransferPolicy
    {
        public static readonly BlockingTransferPolicy Instance = new BlockingTransferPolicy();
        public ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context) => ConveyorOverflowDisposition.Block;
    }

    public sealed class VaporizingTransferPolicy : IConveyorTransferPolicy
    {
        public static readonly VaporizingTransferPolicy Instance = new VaporizingTransferPolicy();
        public ConveyorOverflowDisposition EvaluateFullDestination(in ConveyorTransferContext context) => ConveyorOverflowDisposition.Vaporize;
    }

    public sealed class TeleportModuleFactory : IConveyorModuleFactory
    {
        private readonly string _destinationId;
        private readonly double _entrance;
        private readonly double _exit;
        private readonly IConveyorTransferPolicy _policy;
        private readonly int[] _laneMap;

        public TeleportModuleFactory(string destinationId, double entrance, double exit,
            IConveyorTransferPolicy policy = null, int[] laneMap = null)
        {
            if (string.IsNullOrWhiteSpace(destinationId)) throw new ArgumentException("Destination ID is required.");
            if (!ConveyorValidation.IsFinite(entrance) || entrance < 0 || entrance >= 1
                || !ConveyorValidation.IsFinite(exit) || exit < 0 || exit >= 1)
                throw new ArgumentOutOfRangeException(nameof(entrance));
            _destinationId = destinationId;
            _entrance = entrance;
            _exit = exit;
            _policy = policy ?? BlockingTransferPolicy.Instance;
            _laneMap = laneMap == null ? null : (int[])laneMap.Clone();
        }

        public IConveyorModule Create() => new TeleportModule(_destinationId, _entrance, _exit, _policy, _laneMap);
    }

    public sealed class TeleportModule : IConveyorModule
    {
        #region Properties
        public ConveyorTransportCapabilities RequiredCapabilities => ConveyorTransportCapabilities.Gates
            | ConveyorTransportCapabilities.LaneHolding | ConveyorTransportCapabilities.AdmissionReservations;
        #endregion

        #region Private Fields
        private readonly string _destinationId;
        private readonly double _entrance;
        private readonly double _exit;
        private readonly IConveyorTransferPolicy _policy;
        private readonly int[] _laneMap;
        private ConveyorModuleContext _context;
        private int[] _gates;
        private int _registeredGates;
        #endregion

        #region Construction
        internal TeleportModule(string destinationId, double entrance, double exit, IConveyorTransferPolicy policy, int[] laneMap)
        {
            _destinationId = destinationId;
            _entrance = entrance;
            _exit = exit;
            _policy = policy;
            _laneMap = laneMap;
        }
        #endregion

        #region Public Methods
        public void HandleRegistered(ConveyorModuleContext context)
        {
            if (_context != null) throw new InvalidOperationException("Module already registered.");
            if (_laneMap != null && _laneMap.Length != context.Runtime.Channels)
                throw new ArgumentException("An explicit lane map needs one destination channel per source lane.");
            if (_destinationId == context.Runtime.Id && Math.Abs(_entrance - _exit) < 0.000001)
                throw new ArgumentException("A same-loop entrance and exit cannot overlap.");
            _context = context;
            _gates = new int[context.Runtime.Channels];
            for (int channel = 0; channel < _gates.Length; channel++)
            {
                if (_laneMap != null && _laneMap[channel] < 0) throw new ArgumentException("Lane mappings cannot be negative.");
                _gates[channel] = context.AddGate(channel, _entrance);
                _registeredGates++;
            }
        }

        public void CollectTransfers(ConveyorTransferRequests requests)
        {
            for (int channel = 0; channel < _registeredGates; channel++)
            {
                if (!_context.TryGetWaiting(_gates[channel], out ConveyorPayloadHandle handle)) continue;
                int destinationChannel = _laneMap == null ? channel : _laneMap[channel];
                requests.TryAdd(handle, _gates[channel], _destinationId, destinationChannel, _exit, _policy);
            }
        }

        public void Dispose()
        {
            if (_context == null) return;
            for (int index = 0; index < _registeredGates; index++) _context.RemoveGate(_gates[index]);
            _registeredGates = 0;
            _context = null;
        }
        #endregion
    }
}
