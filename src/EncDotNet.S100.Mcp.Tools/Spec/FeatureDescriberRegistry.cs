using System.Collections.Concurrent;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Lookup table from spec name (canonical <c>"S-NNN"</c>) to the
/// describer that handles it. <see cref="Default"/> is populated with
/// every describer this assembly ships with.
/// </summary>
public sealed class FeatureDescriberRegistry
{
    private readonly ConcurrentDictionary<string, ISpecFeatureDescriber> _byName;

    internal FeatureDescriberRegistry(IEnumerable<ISpecFeatureDescriber> describers)
    {
        ArgumentNullException.ThrowIfNull(describers);
        _byName = new ConcurrentDictionary<string, ISpecFeatureDescriber>(StringComparer.Ordinal);
        foreach (var d in describers)
        {
            _byName[d.SpecName] = d;
        }
    }

    /// <summary>Default registry, containing the describers shipped with this assembly.</summary>
    public static FeatureDescriberRegistry Default { get; } = new FeatureDescriberRegistry(
    [
        new S101FeatureDescriber(),
        new S102FeatureDescriber(),
        new S104FeatureDescriber(),
        new S111FeatureDescriber(),
        new S124FeatureDescriber(),
    ]);

    internal ISpecFeatureDescriber? Get(string specName) =>
        _byName.TryGetValue(specName, out var d) ? d : null;
}
