using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Capability implemented by dataset processors whose portrayal can draw some
/// instructions only at some times (S-100 Part 9 <c>timeValid</c>, such as
/// S-101 features with a fixed or periodic date range). They draw at
/// <see cref="RenderContext.PortrayalTime"/>.
/// </summary>
/// <remarks>
/// A host uses <see cref="PortrayalTimeIntervals"/> to tell the mariner the
/// display depends on the view time (S-98 §12.8.2 "Display Not Real Time")
/// and, with <see cref="PortrayalTimeFilter.Changes"/>, to re-render the
/// dataset only when moving the view time changes what it draws.
/// </remarks>
public interface IDateDependentDatasetProcessor
{
    /// <summary>
    /// The distinct time intervals of the last portrayal's drawing
    /// instructions; empty when none is date-dependent or nothing has been
    /// portrayed yet.
    /// </summary>
    IReadOnlyList<PortrayalTimeInterval> PortrayalTimeIntervals { get; }
}
