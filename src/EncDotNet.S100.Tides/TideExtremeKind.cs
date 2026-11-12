namespace EncDotNet.S100.Tides;

/// <summary>Whether a <see cref="TideExtreme"/> is a high or a low water.</summary>
public enum TideExtremeKind
{
    /// <summary>A high water: the tide stops rising and starts to fall.</summary>
    High,

    /// <summary>A low water: the tide stops falling and starts to rise.</summary>
    Low,
}
