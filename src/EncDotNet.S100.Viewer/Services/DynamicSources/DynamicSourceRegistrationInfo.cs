using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Read-only registration view of an <see cref="EncDotNet.S100.DynamicSources.IDynamicFeatureSource"/>
/// hosted by <see cref="DynamicSourceOverlayHost"/>. Surfaced through
/// <see cref="IDynamicFeatureSourceRegistry"/> so view-models can
/// render Layer Stack rows without taking a dependency on the
/// concrete source instance.
/// </summary>
/// <param name="Id">Instance-unique source id.</param>
/// <param name="DisplayName">Localised display label (PR-D2.1).</param>
/// <param name="Description">Optional longer description for tooltips.</param>
internal sealed record DynamicSourceRegistrationInfo(
    string Id,
    string DisplayName,
    string? Description);
