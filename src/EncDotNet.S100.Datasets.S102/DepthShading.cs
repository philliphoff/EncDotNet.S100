namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// Maps a depth range to a colour for portrayal of bathymetric surfaces.
/// </summary>
public sealed class DepthShading
{
    /// <summary>Minimum depth (inclusive) in metres.</summary>
    public required float MinDepth { get; init; }

    /// <summary>Maximum depth (exclusive) in metres.</summary>
    public required float MaxDepth { get; init; }

    /// <summary>A colour value (e.g. hex <c>#2196F3</c>) for this depth range.</summary>
    public required string Color { get; init; }

    /// <summary>Optional human-readable label for this depth range.</summary>
    public string? Label { get; init; }
}
