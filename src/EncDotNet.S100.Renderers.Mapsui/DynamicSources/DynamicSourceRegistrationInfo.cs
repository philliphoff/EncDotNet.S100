namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Read-only registration view of an
/// <see cref="EncDotNet.S100.DynamicSources.IDynamicFeatureSource"/> hosted by
/// <see cref="S100DynamicSourceHost"/>. Surfaced through
/// <see cref="IS100DynamicSourceRegistry"/> so view-models can render source
/// rows without taking a dependency on the concrete source instance.
/// </summary>
/// <param name="Id">Instance-unique source id.</param>
/// <param name="DisplayName">Display label.</param>
/// <param name="Description">Optional longer description for tooltips.</param>
public sealed record DynamicSourceRegistrationInfo(
    string Id,
    string DisplayName,
    string? Description);
