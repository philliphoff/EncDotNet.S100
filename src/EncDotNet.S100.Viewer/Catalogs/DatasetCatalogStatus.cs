namespace EncDotNet.S100.Viewer.Catalogs;

/// <summary>
/// Neutral, source-agnostic currency status for a <see cref="DatasetCatalogEntry"/>.
/// </summary>
/// <remarks>
/// Each <see cref="IDatasetCatalogSource"/> is responsible for mapping its
/// own status taxonomy onto these values (e.g. S-128's
/// <c>S128ProductStatus</c> maps 1:1 by name).
/// </remarks>
internal enum DatasetCatalogStatus
{
    /// <summary>The status is unknown or could not be determined.</summary>
    Unknown = 0,

    /// <summary>The product is current.</summary>
    InForce,

    /// <summary>The product has been replaced by a newer edition.</summary>
    Superseded,

    /// <summary>The product has been withdrawn or cancelled.</summary>
    Withdrawn,

    /// <summary>The product is announced but not yet released.</summary>
    Planned,
}
