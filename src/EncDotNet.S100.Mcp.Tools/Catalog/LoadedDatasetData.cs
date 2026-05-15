using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S201;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S421;

namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// Discriminated union over the typed payloads that a
/// <see cref="LoadedDataset"/> may carry. Each spec contributes either a
/// typed DataModel projection (vector products) or a coverage source
/// (HDF5-encoded coverage products).
/// </summary>
public abstract record LoadedDatasetData;

/// <summary>S-122 Marine Protected Areas typed model.</summary>
public sealed record S122DatasetData(S122Dataset Model) : LoadedDatasetData;

/// <summary>S-124 Navigational Warnings typed model.</summary>
public sealed record S124DatasetData(S124Dataset Model) : LoadedDatasetData;

/// <summary>S-125 Marine Aids to Navigation typed model.</summary>
public sealed record S125DatasetData(S125Dataset Model) : LoadedDatasetData;

/// <summary>S-127 Marine Resources and Services typed model.</summary>
public sealed record S127DatasetData(S127Dataset Model) : LoadedDatasetData;

/// <summary>S-128 Catalogue of Nautical Products typed model.</summary>
public sealed record S128DatasetData(S128Dataset Model) : LoadedDatasetData;

/// <summary>S-129 Under Keel Clearance typed model.</summary>
public sealed record S129DatasetData(S129Dataset Model) : LoadedDatasetData;

/// <summary>S-201 Aids to Navigation Information typed model.</summary>
public sealed record S201DatasetData(S201Dataset Model) : LoadedDatasetData;

/// <summary>S-411 Sea Ice Information typed model.</summary>
public sealed record S411DatasetData(S411Dataset Model) : LoadedDatasetData;

/// <summary>S-421 Route Plans typed model.</summary>
public sealed record S421DatasetData(S421Dataset Model) : LoadedDatasetData;

/// <summary>S-102 Bathymetric Surface coverage handle.</summary>
/// <remarks>
/// Coverage variants carry a live handle (the <see cref="ICoverageSource"/>
/// or its concrete equivalent). Tool implementations must treat the handle
/// as best-effort: it may be disposed between catalog capture and the
/// actual sample call. Wrap reads in
/// <c>try / catch (ObjectDisposedException)</c> and surface the failure as
/// <see cref="EncDotNet.S100.Mcp.Tools.DatasetClosedDuringQuery"/>.
/// </remarks>
public sealed record S102CoverageData(S102CoverageSource Source) : LoadedDatasetData;

/// <summary>S-104 Water Level coverage handle.</summary>
public sealed record S104CoverageData(S104CoverageSource Source) : LoadedDatasetData;

/// <summary>S-111 Surface Currents coverage handle.</summary>
public sealed record S111CoverageData(S111CoverageSource Source) : LoadedDatasetData;
