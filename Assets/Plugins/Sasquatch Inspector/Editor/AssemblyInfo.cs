using System.Runtime.CompilerServices;

// The verification/stress test assembly compiles separately. Exposing internals to it means
// production types and members no longer need to be `public` solely so cross-assembly tests can
// reach them — keep the real public surface limited to the intended user-facing API.
[assembly: InternalsVisibleTo("SasquatchInspector.Tests.Editor")]
