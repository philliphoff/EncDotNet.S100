// The tiled base-plane renderer tests read process-global RenderingOptimizations
// state and wire global RequestRedraw hooks while they render. Disabling
// cross-class parallelization keeps a concurrent render in another test class
// from observing that shared state mid-render.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
