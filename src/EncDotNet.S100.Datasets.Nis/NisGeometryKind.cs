namespace EncDotNet.S100.Datasets.Nis;

/// <summary>The kind of geometry an NIS feature has.</summary>
public enum NisGeometryKind
{
    /// <summary>One position.</summary>
    Point,

    /// <summary>Several positions, such as a group of soundings.</summary>
    MultiPoint,

    /// <summary>One or more paths.</summary>
    Line,

    /// <summary>One or more rings bounding an area.</summary>
    Area,
}
