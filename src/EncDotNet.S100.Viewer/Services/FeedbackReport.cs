using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Immutable snapshot of the diagnostic data the feedback reporter
/// collects automatically. Everything shown to the user in the dialog's
/// "raw data" section, and everything written to the feedback bundle,
/// comes from this record — there is no hidden collection.
/// </summary>
/// <remarks>
/// The screenshot is deliberately <b>not</b> part of this model: it is a
/// separate, optional PNG that the user can preview and exclude. This
/// record contains only textual diagnostics.
/// </remarks>
internal sealed record FeedbackReport
{
    /// <summary>When the report was generated (UTC, ISO-8601).</summary>
    public required DateTimeOffset GeneratedUtc { get; init; }

    /// <summary>Application identity / build information.</summary>
    public required FeedbackAppInfo Application { get; init; }

    /// <summary>Operating-system and .NET runtime information.</summary>
    public required FeedbackRuntimeInfo Runtime { get; init; }

    /// <summary>Current map viewport, or <see langword="null"/> when the
    /// map has no laid-out viewport yet.</summary>
    public FeedbackViewportInfo? Viewport { get; init; }

    /// <summary>Currently-loaded datasets (may be empty).</summary>
    public required IReadOnlyList<FeedbackDatasetInfo> Datasets { get; init; }

    /// <summary>The most recent unhandled error this session, or
    /// <see langword="null"/> when none was recorded.</summary>
    public FeedbackErrorInfo? LastError { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serialises the report to indented JSON — exactly the text shown in
    /// the dialog's raw-data section and written to the bundle.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>Application identity / build information.</summary>
/// <param name="Name">Product name.</param>
/// <param name="Version">Informational/assembly version string.</param>
/// <param name="Theme">Active chrome theme (e.g. <c>Dark</c>).</param>
/// <param name="Palette">Active S-100 portrayal palette (e.g. <c>Day</c>).</param>
internal sealed record FeedbackAppInfo(
    string Name,
    string Version,
    string Theme,
    string Palette);

/// <summary>Operating-system and .NET runtime information.</summary>
/// <param name="OperatingSystem">OS description string.</param>
/// <param name="Architecture">Process architecture (e.g. <c>Arm64</c>).</param>
/// <param name="FrameworkDescription">.NET runtime description.</param>
/// <param name="Culture">Current UI culture name.</param>
internal sealed record FeedbackRuntimeInfo(
    string OperatingSystem,
    string Architecture,
    string FrameworkDescription,
    string Culture);

/// <summary>Map viewport bounds, in WGS-84 decimal degrees.</summary>
/// <param name="MinLatitude">South edge.</param>
/// <param name="MinLongitude">West edge.</param>
/// <param name="MaxLatitude">North edge.</param>
/// <param name="MaxLongitude">East edge.</param>
internal sealed record FeedbackViewportInfo(
    double MinLatitude,
    double MinLongitude,
    double MaxLatitude,
    double MaxLongitude);

/// <summary>Summary of a single loaded dataset.</summary>
/// <param name="DisplayName">User-facing dataset name (no full path).</param>
/// <param name="ProductSpec">S-1xx product spec code.</param>
/// <param name="IsVisible">Whether the dataset layer is currently shown.</param>
/// <param name="ValidationErrorCount">Validation findings of error severity.</param>
/// <param name="ValidationWarningCount">Validation findings of warning severity.</param>
internal sealed record FeedbackDatasetInfo(
    string DisplayName,
    string ProductSpec,
    bool IsVisible,
    int ValidationErrorCount,
    int ValidationWarningCount);

/// <summary>The most recent unhandled error.</summary>
/// <param name="TimestampUtc">When the error occurred (UTC).</param>
/// <param name="Source">Short origin label.</param>
/// <param name="ExceptionType">CLR exception type, when known.</param>
/// <param name="Message">Error message.</param>
/// <param name="StackTrace">Full exception text / stack trace.</param>
internal sealed record FeedbackErrorInfo(
    DateTimeOffset TimestampUtc,
    string Source,
    string? ExceptionType,
    string Message,
    string? StackTrace);
