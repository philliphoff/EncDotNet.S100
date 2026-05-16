using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S131;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal static class S131Synth
{
    /// <summary>Builds a minimal in-memory S-131 dataset for tests.</summary>
    public static S131Dataset Dataset(params S131Feature[] features) => new()
    {
        Features = features.ToImmutableArray(),
        InformationTypes = ImmutableArray<S131InformationType>.Empty,
    };
}
