namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Marker interface for the typed output of an S-100 portrayal pipeline,
/// implemented by both vector layers (<see cref="Vector.IVectorLayer"/>)
/// and styled coverage layers
/// (<see cref="Coverage.StyledCoverageLayer"/>).
/// </summary>
/// <remarks>
/// The interface intentionally carries no members: vector and coverage
/// layers expose fundamentally different shapes (display lists vs.
/// sampled grids) and renderers consume them via concrete types. The
/// marker exists so the orchestrator (<see cref="PortrayalPipeline"/>)
/// can return a single uniform type and callers can dispatch on the
/// runtime type when they need the underlying surface.
/// </remarks>
public interface IPortrayalLayer
{
}
