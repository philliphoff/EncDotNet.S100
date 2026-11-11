namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// Hint indicating the kind of change a
/// <see cref="DynamicFeaturesChanged"/> event describes. Consumers
/// may use this to apply a diff or simply re-read
/// <see cref="IDynamicFeatureSource.CurrentFeatures"/>.
/// </summary>
public enum DynamicSourceChangeKind
{
    /// <summary>One or more features appeared.</summary>
    Added,

    /// <summary>One or more existing features were updated in place.</summary>
    Updated,

    /// <summary>One or more features were removed.</summary>
    Removed,

    /// <summary>
    /// Wholesale reset — consumer should re-read
    /// <see cref="IDynamicFeatureSource.CurrentFeatures"/> and
    /// rebuild any caches. <c>ChangedIds</c> may be empty.
    /// </summary>
    Reset,
}
