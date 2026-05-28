namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// Event payload published by <see cref="IDynamicFeatureSource.Changed"/>
/// whenever the set of currently-known features changes. Carries a
/// kind hint and the touched ids so that consumers can either apply a
/// diff or simply re-read the snapshot.
/// </summary>
public sealed record DynamicFeaturesChanged
{
    /// <summary>Kind of change.</summary>
    public required DynamicSourceChangeKind Kind { get; init; }

    /// <summary>
    /// Feature ids touched by this change. May be empty for
    /// <see cref="DynamicSourceChangeKind.Reset"/>; otherwise
    /// non-empty.
    /// </summary>
    public IReadOnlyList<string> ChangedIds { get; init; } = Array.Empty<string>();
}
