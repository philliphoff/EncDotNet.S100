namespace EncDotNet.S100.Tides.Stations.Mirror;

/// <summary>
/// Whether a mirror has newer data than a catalogue: the mirror's manifest,
/// compared with the catalogue's retrieval date.
/// </summary>
public sealed class TideCatalogueMirrorCheck
{
    /// <summary>When the check was made.</summary>
    public required DateTimeOffset Checked { get; init; }

    /// <summary>The mirror's manifest.</summary>
    public required TideCatalogueManifest Manifest { get; init; }

    /// <summary>
    /// Whether the mirror's catalogue was retrieved from its provider later
    /// than the catalogue checked, or the catalogue checked is undated or
    /// missing, and this library can read it
    /// (<see cref="TideCatalogueManifest.IsSupported"/>).
    /// </summary>
    public required bool HasNewerData { get; init; }
}
