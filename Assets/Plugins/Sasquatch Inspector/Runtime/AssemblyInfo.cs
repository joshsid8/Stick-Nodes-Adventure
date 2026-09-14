using System.Runtime.CompilerServices;

// Twin of Editor/AssemblyInfo.cs, for the runtime assembly. Exposing internals to the two test
// assemblies means test-only seams (the SpringKeys slot constants, the engine's simulated-width
// accessor) stay `internal` instead of being widened to `public` just to be reachable — the shipped
// public surface stays the intended user-facing API.
[assembly: InternalsVisibleTo("SasquatchInspector.Tests.Editor")]
[assembly: InternalsVisibleTo("SasquatchInspector.Tests.PlayMode")]

// The Editor assembly needs ONE runtime internal: SasquatchDebug.Handler, the seam a user's Record()/Stop()
// call travels through to reach Live Debug. It is deliberately not public — the shipped surface is the four
// methods people call, not the delegate the editor happens to install behind them.
[assembly: InternalsVisibleTo("SasquatchInspector.Editor")]
