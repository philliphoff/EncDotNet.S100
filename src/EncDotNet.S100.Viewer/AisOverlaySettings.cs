namespace EncDotNet.S100.Viewer;

/// <summary>
/// User-configurable AIS-overlay settings (PR-D3). Persisted under
/// <c>ViewerSettings.AisOverlay</c>. The aisstream.io API key may
/// come from one of two sources, in priority order:
///
/// <list type="number">
///   <item>The environment variable named in
///   <see cref="ApiKeyEnvironmentVariable"/> (default
///   <c>ENCDOTNET_AIS_STREAM_KEY</c>) — preferred for users who
///   don't want their key sitting in <c>settings.json</c>.</item>
///   <item><see cref="ApiKey"/> — set via the Settings panel for
///   convenience. Stored in plaintext in the user's
///   <c>settings.json</c>; treat the file with the usual care for
///   single-user secrets.</item>
/// </list>
///
/// If neither yields a value the overlay stays inactive even when
/// <see cref="Enabled"/> is <see langword="true"/>.
/// </summary>
internal sealed class AisOverlaySettings
{
    /// <summary>
    /// When <see langword="true"/> and an API key is available (env
    /// var or <see cref="ApiKey"/>), the viewer registers the AIS
    /// dynamic feature source on startup. When <see langword="false"/>
    /// (the default) the overlay stays inactive — the toggle is the
    /// user's explicit opt-in.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Name of the environment variable holding the user's
    /// aisstream.io API key. Defaults to
    /// <c>ENCDOTNET_AIS_STREAM_KEY</c>.
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; set; } = "ENCDOTNET_AIS_STREAM_KEY";

    /// <summary>
    /// Optional aisstream.io API key persisted directly in
    /// <c>settings.json</c>. Used only when the env var named in
    /// <see cref="ApiKeyEnvironmentVariable"/> is unset or blank.
    /// Stored in plaintext — for shared environments prefer the
    /// env-var path. Default <see langword="null"/>.
    /// </summary>
    public string? ApiKey { get; set; }

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

    /// <summary>
    /// Maximum viewport span — in degrees of latitude AND longitude —
    /// at which the AIS subscription is allowed to start. While the
    /// visible viewport's lat-span or lon-span is wider than this, the
    /// overlay stays inactive and no traffic is fetched from
    /// aisstream.io. Once both spans drop to or below this threshold
    /// the subscription is created with the live viewport bbox and
    /// stays active for the rest of the viewer session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default <c>50.0</c> — wide enough to admit any realistic
    /// regional view (e.g. North Sea ~10° × 6°, Mediterranean
    /// ~40° × 14°) while excluding the cold-start global view
    /// (typically 360° × 170° on a fresh viewer).
    /// </para>
    /// <para>
    /// <see langword="null"/> disables the gate entirely — the
    /// subscription starts immediately on viewer launch, matching
    /// pre-PR behaviour. Values <c>&lt;= 0</c> are normalised to
    /// <see langword="null"/> by the settings view-model so users
    /// cannot configure a gate that never opens.
    /// </para>
    /// </remarks>
    public double? ActivationViewportSpanDegrees { get; set; } = 50.0;
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
