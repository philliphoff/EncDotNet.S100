using System.Collections.Generic;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Immutable descriptor for one exchange-set cell to register in bulk via
/// <see cref="DatasetsViewModel.AddRangeFromExchangeSet"/>. Mirrors the
/// parameters of <see cref="DatasetsViewModel.AddFromExchangeSet"/> so a large
/// set can be registered with a single collection notification (issue #458).
/// </summary>
/// <param name="Source">The asset source the cell's bytes live in.</param>
/// <param name="RelativePath">The cell's path relative to the source root.</param>
/// <param name="ProductSpec">The product specification (e.g. <c>"S-57"</c>).</param>
/// <param name="DisplayName">The cell's display name, or <see langword="null"/>
/// to derive it from the path.</param>
/// <param name="UpdateRelativePaths">In-set sequential update file paths.</param>
/// <param name="MinimumDisplayScale">Coarsest display scale, if known.</param>
/// <param name="MaximumDisplayScale">Finest display scale, if known.</param>
/// <param name="GeographicBounds">The cell's EPSG:4326 footprint from the
/// catalogue, used for viewport culling.</param>
internal sealed record ExchangeSetCellRegistration(
    IAssetSource Source,
    string RelativePath,
    string ProductSpec,
    string? DisplayName = null,
    IReadOnlyList<string>? UpdateRelativePaths = null,
    int? MinimumDisplayScale = null,
    int? MaximumDisplayScale = null,
    ExchangeSets.BoundingBox? GeographicBounds = null);
