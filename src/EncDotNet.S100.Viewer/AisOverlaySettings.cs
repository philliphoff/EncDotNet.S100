namespace EncDotNet.S100.Viewer;

/// <summary>
/// User-configurable AIS-overlay settings (PR-D3). Persisted under
/// <c>ViewerSettings.AisOverlay</c>. The API key is **not** stored
/// in <c>settings.json</c> — it is read at startup from the
/// environment variable named in
/// <see cref="ApiKeyEnvironmentVariable"/> (default
/// <c>ENCDOTNET_AIS_STREAM_KEY</c>) so users can enable the overlay
/// without committing their key into a shared config file.
/// </summary>
internal sealed class AisOverlaySettings
{
    /// <summary>
    /// When <see langword="true"/> and the configured API-key
    /// environment variable is set, the viewer registers the AIS
    /// dynamic feature source on startup. When <see langword="false"/>
    /// (the default), the overlay stays inactive even if the env var
    /// is set — the toggle is the user's explicit opt-in.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Name of the environment variable holding the user's
    /// aisstream.io API key. Defaults to
    /// <c>ENCDOTNET_AIS_STREAM_KEY</c>.
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; set; } = "ENCDOTNET_AIS_STREAM_KEY";

    /// <summary>
    /// Optional initial bounding box (minLat, minLon, maxLat, maxLon)
    /// pushed to aisstream.io as the subscription's spatial filter.
    /// When <see langword="null"/>, the driver uses the world bbox
    /// (which aisstream.io treats as "all global traffic" — heavy).
    /// The viewer is expected to override this with the live viewport
    /// bbox via <c>UpdateArea</c>; this seed value just bounds the
    /// initial subscribe before the user has panned the map.
    /// </summary>
    public AisOverlayBoundingBox? InitialArea { get; set; }
}

/// <summary>
/// Plain-data bounding box for <see cref="AisOverlaySettings.InitialArea"/>.
/// Mirrored separately from <see cref="EncDotNet.S100.Core.BoundingBox"/>
/// so the settings POCO has no dependency on Core types.
/// </summary>
internal sealed class AisOverlayBoundingBox
{
    public double MinLatitude { get; set; }
    public double MinLongitude { get; set; }
    public double MaxLatitude { get; set; }
    public double MaxLongitude { get; set; }
}
