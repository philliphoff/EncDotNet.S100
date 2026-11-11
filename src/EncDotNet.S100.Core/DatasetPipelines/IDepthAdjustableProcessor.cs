namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A processor whose portrayal changes with <see cref="RenderContext.DepthAdjustment"/>.
/// A host re-renders only these processors when the water level it applies
/// changes, for example as the Timeline moves.
/// </summary>
public interface IDepthAdjustableProcessor
{
}
