namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A plain line a feature type's rule draws without a catalogue line style:
/// Lua's <c>SimpleLineStyle</c> or <c>LineStyle:_simple_</c>, or an XSLT
/// <c>lineStyle</c> with a <c>pen</c>.
/// </summary>
public sealed class PenUse : RuleUse
{
    /// <summary>The dash style, such as <c>solid</c>, <c>dash</c> or <c>dot</c>.</summary>
    public required string Dash { get; init; }

    /// <summary>The pen width in millimetres.</summary>
    public double Width { get; init; }

    /// <summary>The colour token, such as <c>CHGRD</c>.</summary>
    public required string Token { get; init; }
}
