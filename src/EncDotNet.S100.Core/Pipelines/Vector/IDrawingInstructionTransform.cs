namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Post-parse transform applied to the typed drawing instructions produced by
/// a rule executor, before they are returned to the pipeline. Operates on
/// <see cref="DrawingInstruction"/> values (not raw emit strings), so it can
/// merge, reorder, or rewrite already-parsed instructions.
/// </summary>
/// <remarks>
/// This seam runs <i>after</i> parse, distinct from
/// <see cref="IFeatureAnchorProvider"/> which feeds the parse itself. The
/// S-101 SAFCON contour-label merger is the canonical implementation; products
/// that need no post-processing (e.g. S-131) supply an empty transform list.
/// </remarks>
public interface IDrawingInstructionTransform
{
    /// <summary>
    /// Returns a transformed instruction list. Implementations must not mutate
    /// the input; they return a new list (or the same instance unchanged).
    /// </summary>
    /// <param name="instructions">The parsed instructions to transform.</param>
    IReadOnlyList<DrawingInstruction> Transform(IReadOnlyList<DrawingInstruction> instructions);
}
