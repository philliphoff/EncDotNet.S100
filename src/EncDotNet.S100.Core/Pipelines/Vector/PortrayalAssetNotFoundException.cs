namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// The kind of named portrayal asset a lookup was for. Used by
/// <see cref="PortrayalAssetNotFoundException"/> so a handler can react to the
/// category of the missing asset without parsing the message.
/// </summary>
public enum PortrayalAssetKind
{
    /// <summary>An SVG symbol (S-100 Part 9 §11).</summary>
    Symbol,

    /// <summary>A line style.</summary>
    LineStyle,

    /// <summary>An area fill.</summary>
    AreaFill,

    /// <summary>A portrayal rule (S-100 Part 9 §9.4).</summary>
    Rule,
}

/// <summary>
/// Thrown when a portrayal catalogue is asked for a named asset — an SVG
/// symbol, line style, area fill, or portrayal rule — that the loaded
/// catalogue does not contain.
/// </summary>
/// <remarks>
/// <para>
/// A miss here is a contract violation, not an expected absence: a drawing
/// instruction or rule referenced an asset the packaged catalogue should have
/// carried, so there is no meaningful recovery at the lookup site and the
/// method fails fast (see the missing-value policy in
/// <c>docs/design/api-conventions.md</c>). It derives from
/// <see cref="Exception"/> — the recommended base for a domain exception —
/// rather than <see cref="KeyNotFoundException"/>, so it is not swept up by
/// handlers that guard ordinary dictionary lookups.
/// </para>
/// <para>
/// Callers that must tolerate a missing asset (for example the render-time
/// catalogue pre-warm, which treats any lookup failure as "not in catalogue")
/// should catch this explicitly.
/// </para>
/// </remarks>
public sealed class PortrayalAssetNotFoundException : Exception
{
    /// <summary>The category of asset that was requested.</summary>
    public PortrayalAssetKind AssetKind { get; }

    /// <summary>The catalogue name that was looked up and not found.</summary>
    public string AssetName { get; }

    /// <summary>
    /// Initializes a new <see cref="PortrayalAssetNotFoundException"/> for the
    /// given asset kind and name.
    /// </summary>
    /// <param name="assetKind">The category of the missing asset.</param>
    /// <param name="assetName">The catalogue name that was not found.</param>
    /// <param name="innerException">The optional underlying cause.</param>
    public PortrayalAssetNotFoundException(
        PortrayalAssetKind assetKind,
        string assetName,
        Exception? innerException = null)
        : base(FormatMessage(assetKind, assetName), innerException)
    {
        ArgumentNullException.ThrowIfNull(assetName);
        AssetKind = assetKind;
        AssetName = assetName;
    }

    private static string FormatMessage(PortrayalAssetKind assetKind, string assetName)
    {
        var label = assetKind switch
        {
            PortrayalAssetKind.Symbol => "Symbol",
            PortrayalAssetKind.LineStyle => "Line style",
            PortrayalAssetKind.AreaFill => "Area fill",
            PortrayalAssetKind.Rule => "Rule",
            _ => "Portrayal asset",
        };

        return $"{label} '{assetName}' was not found in the portrayal catalogue.";
    }
}
