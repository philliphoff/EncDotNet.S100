namespace EncDotNet.S100.Diagnostics;

/// <summary>
/// Standard <see cref="System.Diagnostics.Activity"/> tag and
/// <see cref="System.Diagnostics.Metrics.Meter"/> dimension keys used
/// across EncDotNet.S100 telemetry. Centralising them prevents drift
/// between libraries and lets dashboards / collectors rely on a single
/// vocabulary.
/// </summary>
/// <remarks>
/// Naming follows OpenTelemetry semantic-convention style:
/// lowercase, dot-separated, namespaced under <c>s100.</c>. New tags
/// added by individual libraries should follow the same shape so that
/// querying is uniform.
/// </remarks>
public static class TelemetryTags
{
    /// <summary>S-100 product identifier, e.g. <c>S-101</c>, <c>S-102</c>, <c>S-124</c>.</summary>
    public const string Product = "s100.product";

    /// <summary>Absolute or producer-supplied dataset path (sanitised by host before export if needed).</summary>
    public const string DatasetPath = "s100.dataset.path";

    /// <summary>Pipeline stage: <c>read</c>, <c>portray</c>, or <c>render</c>.</summary>
    public const string PipelineStage = "s100.pipeline.stage";

    /// <summary>Vector pipeline feature type / acronym (e.g. <c>DEPARE</c>, <c>BOYLAT</c>).</summary>
    public const string FeatureType = "s100.feature.type";

    /// <summary>HDF5 dataCodingFormat code (1..7 per S-100 Part 10c).</summary>
    public const string CoverageFormat = "s100.coverage.format";

    /// <summary>Map viewport zoom level (Mapsui "resolution" or equivalent).</summary>
    public const string ViewportZoom = "s100.viewport.zoom";

    /// <summary>Map viewport scale denominator (e.g. 50000 for 1:50,000).</summary>
    public const string ViewportScale = "s100.viewport.scale";

    /// <summary>Outcome tag for spans / metric counters: <c>ok</c>, <c>error</c>, <c>skipped</c>.</summary>
    public const string Result = "s100.result";

    /// <summary>Lua portrayal rule name (S-100 Part 9A).</summary>
    public const string LuaRule = "s100.lua.rule";

    /// <summary>Viewer command name for the top-level user-action activity.</summary>
    public const string ViewerCommand = "s100.viewer.command";
}
