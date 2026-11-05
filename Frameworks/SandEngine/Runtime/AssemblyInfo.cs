using System.Runtime.CompilerServices;

// Solver diagnostics/parity fixtures are the only consumers of implementation
// details. Production game code uses the public framework contracts.
[assembly: InternalsVisibleTo("Com.Scheherazade.SandEngine.EditorTests")]
[assembly: InternalsVisibleTo("Com.PMA.SandSim.Engine.Tests")]
