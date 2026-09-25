namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// The storage type of a coverage value field in the source dataset
/// (<see cref="CoverageValueField.Type"/>). Sampled values are always
/// delivered as <c>float</c> in <see cref="SampledCoverage.Values"/>
/// regardless of this type.
/// </summary>
public enum CoverageValueType
{
    /// <summary>32-bit IEEE floating point.</summary>
    Float,

    /// <summary>64-bit IEEE floating point.</summary>
    Double,

    /// <summary>32-bit signed integer.</summary>
    Int,

    /// <summary>32-bit unsigned integer.</summary>
    UInt,

    /// <summary>16-bit signed integer.</summary>
    Short,

    /// <summary>8-bit unsigned integer.</summary>
    Byte
}
