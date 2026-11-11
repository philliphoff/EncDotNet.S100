using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// Turns the packed integers of Section 7 into values:
/// <c>Y = (R + X × 2^E) × 10^−D</c> (WMO FM 92 GRIB, regulation 92.9.4).
/// </summary>
internal readonly struct Scaling
{
    private readonly double _reference;
    private readonly double _binary;
    private readonly double _decimal;

    public Scaling(GribDataRepresentation drs)
    {
        _reference = drs.ReferenceValue;
        _binary = Math.Pow(2, drs.BinaryScaleFactor);
        _decimal = Math.Pow(10, -drs.DecimalScaleFactor);
    }

    public float Apply(long x) => (float)(((x * _binary) + _reference) * _decimal);

    /// <summary>
    /// The value of every point of a field packed in 0 bits: <c>R</c> itself,
    /// unscaled. That's how ecCodes writes and reads constant fields (checked
    /// with 2.50.0 for templates 5.0 and 5.40): it stores the value as
    /// <c>R</c> and leaves <c>D</c> as it was, so applying <c>10^−D</c>
    /// would be wrong by that factor.
    /// </summary>
    public static float Constant(GribDataRepresentation drs) => drs.ReferenceValue;
}

/// <summary>Decodes data representation template 5.0, simple packing.</summary>
internal static class SimpleUnpacking
{
    public static float[] Unpack(ReadOnlySpan<byte> data, GribDataRepresentation drs, CancellationToken cancellationToken)
    {
        var count = PackedCount(drs);
        var scaling = new Scaling(drs);
        var values = new float[count];
        var bits = drs.BitsPerValue;
        if (bits == 0)
        {
            Array.Fill(values, Scaling.Constant(drs));
            return values;
        }

        var reader = new BitReader(data);
        for (var i = 0; i < count; i++)
        {
            if ((i & 0xFFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            values[i] = scaling.Apply(reader.Read(bits));
        }

        return values;
    }

    internal static int PackedCount(GribDataRepresentation drs)
    {
        if (drs.NumberOfPackedValues > int.MaxValue)
        {
            throw new UnsupportedGribException(
                string.Create(CultureInfo.InvariantCulture, $"A field of {drs.NumberOfPackedValues} values is too large to decode."));
        }

        return (int)drs.NumberOfPackedValues;
    }
}

/// <summary>
/// Decodes data representation templates 5.2 (complex packing) and 5.3
/// (complex packing with spatial differencing).
/// </summary>
/// <remarks>
/// <para>Section 7 holds, each part starting on an octet boundary:</para>
/// <list type="number">
/// <item><description>
/// For 5.3 only, the extra descriptors, each <c>ExtraDescriptorOctets</c>
/// octets, sign and magnitude: the first value (and, for order 2, the second),
/// then the overall minimum of the differences.
/// </description></item>
/// <item><description>NG group references, <c>BitsPerValue</c> bits each.</description></item>
/// <item><description>NG group widths, <c>GroupWidthBits</c> bits each, added to <c>GroupWidthReference</c>.</description></item>
/// <item><description>
/// NG scaled group lengths, <c>GroupLengthBits</c> bits each: a length is
/// <c>GroupLengthReference + stored × GroupLengthIncrement</c>, except the last
/// group, whose length is <c>LastGroupLength</c>.
/// </description></item>
/// <item><description>The values, group after group with no padding: each is the group reference plus <c>width</c> bits.</description></item>
/// </list>
/// <para>
/// With missing value management 1, a value of all ones (for its group's
/// width) is missing; with 2, all ones less one is a second kind of missing.
/// A group of width 0 is all missing when its reference is all ones for
/// <c>BitsPerValue</c> bits (or all ones less one, for 2). Missing values
/// decode to <see cref="float.NaN"/>.
/// </para>
/// <para>
/// For 5.3 the integers are then undifferenced, skipping missing values:
/// order 1 sets the first value to <c>h1</c> and each later one to
/// <c>X + min + previous</c>; order 2 sets the first two to <c>h1</c> and
/// <c>h2</c> and each later one to <c>X + min + 2 × previous − one before</c>.
/// The stored values in the first one or two positions are placeholders.
/// </para>
/// </remarks>
internal static class ComplexUnpacking
{
    public static float[] Unpack(ReadOnlySpan<byte> data, GribDataRepresentation drs, CancellationToken cancellationToken)
    {
        var p = drs.Complex ?? throw new InvalidOperationException("Complex packing parameters are missing.");
        var count = SimpleUnpacking.PackedCount(drs);
        var scaling = new Scaling(drs);
        var values = new float[count];
        if (count == 0)
            return values;

        if (p.NumberOfGroups > count || p.NumberOfGroups == 0)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"Complex packing has {p.NumberOfGroups} groups for {count} values."));
        }

        var groups = (int)p.NumberOfGroups;
        var order = p.SpatialDifferencingOrder;

        // Extra descriptors for spatial differencing.
        long first = 0, second = 0, minimum = 0;
        var start = 0;
        if (order > 0)
        {
            var octets = p.ExtraDescriptorOctets;
            start = (order + 1) * octets;
            if (data.Length < start)
                throw new GribFormatException("The data section (Section 7) is too short for the spatial differencing descriptors.");
            first = GribBinary.SignedInt(data, 0, octets);
            if (order == 2)
                second = GribBinary.SignedInt(data, octets, octets);
            minimum = GribBinary.SignedInt(data, order * octets, octets);
        }

        var reader = new BitReader(data[start..]);
        var references = new uint[groups];
        for (var g = 0; g < groups; g++)
            references[g] = reader.Read(drs.BitsPerValue);
        reader.AlignToOctet();

        var widths = new int[groups];
        for (var g = 0; g < groups; g++)
        {
            var width = p.GroupWidthReference + (long)reader.Read(p.GroupWidthBits);
            if (width > 32)
            {
                throw new GribFormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Complex packing group {g} is {width} bits wide; at most 32 are supported."));
            }

            widths[g] = (int)width;
        }

        reader.AlignToOctet();

        var lengths = new int[groups];
        long total = 0;
        for (var g = 0; g < groups; g++)
        {
            long length = p.GroupLengthReference + ((long)reader.Read(p.GroupLengthBits) * p.GroupLengthIncrement);
            if (g == groups - 1)
                length = p.LastGroupLength;
            total += length;
            if (total > count)
                break;
            lengths[g] = (int)length;
        }

        if (total != count)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"Complex packing's group lengths add up to {total}, not the {count} values Section 5 gives."));
        }

        reader.AlignToOctet();

        var integers = new long[count];
        var missing = p.MissingValueManagement == GribMissingValueManagement.None ? null : new bool[count];
        var referenceAllOnes = drs.BitsPerValue == 0 ? 0u : (uint)((1UL << drs.BitsPerValue) - 1);
        var n = 0;
        for (var g = 0; g < groups; g++)
        {
            if ((g & 0x3FF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var reference = references[g];
            var width = widths[g];
            var length = lengths[g];
            if (missing is null)
            {
                for (var k = 0; k < length; k++)
                    integers[n++] = reference + (long)reader.Read(width);
            }
            else if (width == 0)
            {
                var isMissing = reference == referenceAllOnes
                    || (p.MissingValueManagement == GribMissingValueManagement.PrimaryAndSecondary && reference == referenceAllOnes - 1);
                for (var k = 0; k < length; k++)
                {
                    missing[n] = isMissing;
                    integers[n++] = reference;
                }
            }
            else
            {
                var allOnes = (uint)((1UL << width) - 1);
                for (var k = 0; k < length; k++)
                {
                    var x = reader.Read(width);
                    if (x == allOnes || (p.MissingValueManagement == GribMissingValueManagement.PrimaryAndSecondary && x == allOnes - 1))
                        missing[n] = true;
                    integers[n++] = reference + (long)x;
                }
            }
        }

        if (order > 0)
            Undifference(integers, missing, order, first, second, minimum);

        for (var i = 0; i < count; i++)
            values[i] = missing is not null && missing[i] ? float.NaN : scaling.Apply(integers[i]);

        return values;
    }

    private static void Undifference(long[] integers, bool[]? missing, int order, long first, long second, long minimum)
    {
        // The chain runs over the values that aren't missing.
        var seen = 0;
        long previous = 0, beforePrevious = 0;
        for (var i = 0; i < integers.Length; i++)
        {
            if (missing is not null && missing[i])
                continue;

            long value;
            if (seen == 0)
                value = first;
            else if (seen == 1 && order == 2)
                value = second;
            else if (order == 1)
                value = integers[i] + minimum + previous;
            else
                value = integers[i] + minimum + (2 * previous) - beforePrevious;

            integers[i] = value;
            beforePrevious = previous;
            previous = value;
            seen++;
        }
    }
}

/// <summary>Decodes data representation template 5.40, JPEG 2000 packing.</summary>
/// <remarks>
/// Section 7 is a JPEG 2000 codestream holding one greyscale component
/// whose samples, read row by row, are the packed integers. NCEP's encoders
/// write the values as one row (width = the packed count, height 1) when
/// there's a bitmap and as Ni × Nj otherwise; either works, as only the
/// order matters. The type of compression (lossless or lossy) and target
/// ratio in the template need nothing from the decoder: the codestream says
/// which transform it uses. A depth of 0 means every value is <c>R</c>, and
/// encoders then write an empty Section 7, as ecCodes does.
/// </remarks>
internal static class Jpeg2000Unpacking
{
    public static float[] Unpack(ReadOnlySpan<byte> data, GribDataRepresentation drs, CancellationToken cancellationToken)
    {
        var count = SimpleUnpacking.PackedCount(drs);
        var scaling = new Scaling(drs);
        var values = new float[count];
        if (drs.BitsPerValue == 0 || count == 0)
        {
            Array.Fill(values, Scaling.Constant(drs));
            return values;
        }

        var image = Jpeg2000.Jpeg2000Decoder.Decode(data, cancellationToken);
        if ((long)image.Width * image.Height < count)
        {
            throw new GribFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The JPEG 2000 image has {image.Width} × {image.Height} samples, fewer than the {count} values Section 5 gives."));
        }

        var samples = image.Samples;
        for (var i = 0; i < count; i++)
            values[i] = scaling.Apply(samples[i]);

        return values;
    }
}

/// <summary>Decodes data representation template 5.41, PNG packing.</summary>
/// <remarks>
/// Section 7 is a PNG image whose pixels, read row by row, are the packed
/// integers: 1 to 16 bits for grey images, 24 for RGB and 32 for RGBA, the
/// channels taken together, most significant first. The pixels are read at
/// the image's own depth. NCEP's g2lib writes that depth in the template;
/// ecCodes writes the field's bits per value (for example 11) and rounds the
/// image up to 16 bits, so the template's depth is only checked not to
/// exceed the image's. A depth of 0 means every value is <c>R</c> and the
/// section may be empty.
/// </remarks>
internal static class PngUnpacking
{
    public static float[] Unpack(ReadOnlySpan<byte> data, GribDataRepresentation drs, CancellationToken cancellationToken)
    {
        var count = SimpleUnpacking.PackedCount(drs);
        var scaling = new Scaling(drs);
        var values = new float[count];
        if (drs.BitsPerValue == 0 || count == 0)
        {
            Array.Fill(values, Scaling.Constant(drs));
            return values;
        }

        var image = PngReader.Read(data);
        if (image.BitsPerPixel < drs.BitsPerValue)
        {
            throw new GribFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The PNG image has {image.BitsPerPixel} bits per pixel, fewer than template 5.41's depth of {drs.BitsPerValue}."));
        }

        if ((long)image.Width * image.Height < count)
        {
            throw new GribFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The PNG image has {image.Width} × {image.Height} pixels, fewer than the {count} values Section 5 gives."));
        }

        var n = 0;
        for (var row = 0; row < image.Height && n < count; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reader = new BitReader(image.Row(row));
            for (var col = 0; col < image.Width && n < count; col++)
                values[n++] = scaling.Apply(reader.Read(image.BitsPerPixel));
        }

        return values;
    }
}
