using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Diagnostics.Export;

/// <summary>
/// Shared JSON serialisation options and helpers used by
/// <see cref="FileTelemetryExporter"/> and
/// <see cref="FileMetricsExporter"/> for the newline-delimited JSON
/// (<c>.jsonl</c>) telemetry file format.
/// </summary>
internal static class TelemetryJsonFormat
{
    /// <summary>
    /// Current schema version. Bumped when the JSON shape changes in a
    /// way that <c>perfreport</c> must know about.
    /// </summary>
    public const int SchemaVersion = 1;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
