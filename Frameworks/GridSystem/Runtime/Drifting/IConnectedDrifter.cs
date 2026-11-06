using System.Collections.Generic;

namespace Com.Scheherazade.Common.Frameworks.GridSystem
{
    public interface IConnectedDrifter
    {
        IEnumerable<IDrifter> ConnectedDrifters { get; }
    }
}
