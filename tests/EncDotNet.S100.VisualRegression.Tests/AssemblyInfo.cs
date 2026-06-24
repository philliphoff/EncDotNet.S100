using Xunit;

// The "B" (TiledScene) parity tests flip a process-global subsystem flag
// (RenderingOptimizations.RenderSubsystem) and wire global RequestRedraw hooks
// while they render. Disabling cross-class parallelization keeps a concurrent
// "A" render in another test class from observing the flipped flag mid-render.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
