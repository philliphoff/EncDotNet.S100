namespace EncDotNet.S100.Datasets.Pipelines.Sampling;

/// <summary>
/// The products this assembly samples through <see cref="ICoverageSampler"/>.
/// <c>sample_coverage</c> treats them as supported even when none of their
/// datasets is loaded, so such a request reports no coverage rather than an
/// unsupported spec. Any other product is supported once one of its datasets
/// is loaded with a sampler.
/// </summary>
internal static class BuiltInCoverageSamplers
{
    /// <summary>The spec names (S-102, S-104 and S-111).</summary>
    public static IReadOnlySet<string> Specs { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        S102CoverageSampler.Spec,
        S104GridSampler.Spec,
        S111GridSampler.Spec,
    };
}
