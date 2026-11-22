namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>What the Chart reference list shows (design note §4.5).</summary>
internal enum ChartReferenceMode
{
    /// <summary>Feature types, each with its lead glyph.</summary>
    Features,

    /// <summary>Symbols, line styles, area fills and symbol families.</summary>
    Symbols,
}
