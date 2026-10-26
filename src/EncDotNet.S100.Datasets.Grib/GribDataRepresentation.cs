namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// The data representation section (Section 5) of a GRIB2 field: how its
/// values are packed.
/// </summary>
/// <remarks>
/// <para>
/// Every supported template stores integers <c>X</c> that decode to
/// <c>Y = (R + X × 2^E) / 10^D</c>, with <see cref="ReferenceValue"/> <c>R</c>,
/// <see cref="BinaryScaleFactor"/> <c>E</c> and
/// <see cref="DecimalScaleFactor"/> <c>D</c>. The templates differ in how the
/// <c>X</c> are stored:
/// </para>
/// <list type="table">
/// <listheader><term>Template</term><description>Packing</description></listheader>
/// <item><term>5.0</term><description>Simple: each value in <see cref="BitsPerValue"/> bits.</description></item>
/// <item><term>5.2</term><description>Complex: values in groups, each with its own reference and bit width.</description></item>
/// <item><term>5.3</term><description>Complex, packing the first or second differences of the values.</description></item>
/// <item><term>5.41</term><description>A PNG image whose pixels are the values, <see cref="BitsPerValue"/> bits deep.</description></item>
/// </list>
/// <para>
/// Other templates are listed with their number, and the field's
/// <see cref="GribField.UnsupportedReason"/> names them: for example 5.40
/// (JPEG 2000), 5.42 (CCSDS) and 5.4 (IEEE floating point).
/// </para>
/// </remarks>
public sealed class GribDataRepresentation
{
    internal GribDataRepresentation(
        int templateNumber,
        long numberOfPackedValues,
        float referenceValue,
        int binaryScaleFactor,
        int decimalScaleFactor,
        int bitsPerValue,
        byte originalFieldType,
        Internal.ComplexPackingParameters? complex)
    {
        TemplateNumber = templateNumber;
        NumberOfPackedValues = numberOfPackedValues;
        ReferenceValue = referenceValue;
        BinaryScaleFactor = binaryScaleFactor;
        DecimalScaleFactor = decimalScaleFactor;
        BitsPerValue = bitsPerValue;
        OriginalFieldType = originalFieldType;
        Complex = complex;
    }

    /// <summary>The data representation template number (code table 5.0).</summary>
    public int TemplateNumber { get; }

    /// <summary>
    /// The number of values packed in Section 7: the grid points less those
    /// the bitmap marks missing.
    /// </summary>
    public long NumberOfPackedValues { get; }

    /// <summary>The reference value <c>R</c>.</summary>
    public float ReferenceValue { get; }

    /// <summary>The binary scale factor <c>E</c>.</summary>
    public int BinaryScaleFactor { get; }

    /// <summary>The decimal scale factor <c>D</c>.</summary>
    public int DecimalScaleFactor { get; }

    /// <summary>
    /// The bits per packed value (for complex packing, per group reference;
    /// for PNG, the image depth). <c>0</c> means every value is <c>R</c>.
    /// </summary>
    public int BitsPerValue { get; }

    /// <summary>The type of the original field values (code table 5.1): <c>0</c> floating point, <c>1</c> integer.</summary>
    public byte OriginalFieldType { get; }

    /// <summary>For templates 5.2 and 5.3, how missing values are embedded in the packed data; otherwise <see cref="GribMissingValueManagement.None"/>.</summary>
    public GribMissingValueManagement MissingValueManagement => Complex?.MissingValueManagement ?? GribMissingValueManagement.None;

    /// <summary>For template 5.3, the order of spatial differencing (1 or 2); otherwise <c>0</c>.</summary>
    public int SpatialDifferencingOrder => Complex?.SpatialDifferencingOrder ?? 0;

    /// <summary>For templates 5.2 and 5.3, the number of groups; otherwise <c>0</c>.</summary>
    public long NumberOfGroups => Complex?.NumberOfGroups ?? 0;

    internal Internal.ComplexPackingParameters? Complex { get; }
}
