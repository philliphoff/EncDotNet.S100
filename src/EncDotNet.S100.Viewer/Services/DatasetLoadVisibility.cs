using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Decides whether a newly loaded dataset defaults to hidden in the Datasets
/// list, for products whose portrayal would otherwise dominate the chart
/// uninvited. The user can show any of them from the eye icon.
/// </summary>
/// <remarks>
/// The default applies only on the session's first load. Lazy unload keeps
/// session state and its order slot, so a reload preserves a dataset the
/// user had chosen to show (issue #483).
/// </remarks>
internal static class DatasetLoadVisibility
{
    /// <summary>
    /// <see langword="true"/> when <paramref name="processor"/> should load
    /// hidden.
    /// </summary>
    /// <param name="processor">The processor that was just registered.</param>
    /// <param name="fromExchangeSet">
    /// <see langword="true"/> when the dataset came from an exchange set
    /// rather than a file the user opened directly.
    /// </param>
    /// <param name="wasKnownBySession">
    /// <see langword="true"/> when the session already held state for the
    /// dataset (a lazy reload); the user's earlier choice then stands.
    /// </param>
    public static bool LoadsHidden(
        IDatasetProcessor processor,
        bool fromExchangeSet,
        bool wasKnownBySession)
    {
        if (wasKnownBySession)
            return false;

        return processor switch
        {
            // S-104 gridded (dcf2) water-level surfaces are a synthesised,
            // non-normative colour-band heatmap. S-104 Edition 2.0.0 defines
            // no official portrayal catalogue and treats water level as ECDIS
            // vertical-adjustment input, not a chart layer (issue #483).
            // Fixed-station (dcf8) glyphs are discrete symbols at genuine
            // stations and stay visible.
            S104DatasetProcessor { IsGriddedSurface: true } => true,

            // An S-128 Catalogue of Nautical Products bundled in an exchange
            // set describes the set's other products. The upstream IHO S-128
            // portrayal catalogue fills each product's coverage with a 70 %
            // opaque colour on an overlay plane above the ENC, so overlapping
            // products (a harbour cell inside an approach cell) stack to
            // near-opaque over the chart. S-98 Main §9.2.1 says such data
            // must not obscure the ENC's colour fills, and the catalogue is
            // already browsable in the Library panel. A file the user opens on
            // its own is shown, because seeing the coverage is why they opened it.
            S128DatasetProcessor => fromExchangeSet,

            _ => false,
        };
    }
}
