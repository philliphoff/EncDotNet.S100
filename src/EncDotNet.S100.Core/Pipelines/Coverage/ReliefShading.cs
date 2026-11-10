namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Hillshade (relief shading) for a gridded depth surface: a per-cell
/// brightness factor from the surface slope under a point light, which a
/// coverage renderer multiplies into the cell's band colour.
/// </summary>
/// <remarks>
/// <para>
/// Relief shading is not part of any S-100 portrayal: the S-102 Edition 3.0.0
/// portrayal (Annex B) fills each cell with the flat colour of its depth band.
/// It is an optional, off-by-default display aid, so the band colours keep
/// their meaning: a factor of 1 leaves the colour unchanged, a factor below 1
/// darkens it towards black and a factor above 1 lightens it towards white.
/// </para>
/// <para>
/// The slope uses Horn's 3×3 method over the elevation (the negated depth).
/// Grid row 0 is the southernmost row and column 0 the westernmost, as in
/// <see cref="SampledCoverage"/>. Cells next to no-data (or the grid edge) use
/// the centre value in place of the missing neighbour, so the surface does not
/// grow a dark rim along its data limit.
/// </para>
/// </remarks>
public sealed record ReliefShading
{
    /// <summary>The default shading: light from the north-west at 45°, 3× vertical exaggeration, strength 0.6.</summary>
    public static ReliefShading Default { get; } = new();

    /// <summary>The direction the light comes from, in degrees clockwise from north. 315 (north-west) is the cartographic convention.</summary>
    public double AzimuthDegrees { get; init; } = 315.0;

    /// <summary>The light's elevation above the horizon, in degrees (0–90).</summary>
    public double AltitudeDegrees { get; init; } = 45.0;

    /// <summary>
    /// Multiplies the depth differences before the slope is taken. The seabed
    /// is gentle compared with land, so a factor above 1 brings out sand waves,
    /// wrecks and channel edges.
    /// </summary>
    public double VerticalExaggeration { get; init; } = 3.0;

    /// <summary>
    /// How strongly the shading changes the band colour, from 0 (no change) to 1
    /// (full hillshade). Kept below 1 by default so the band colours stay readable.
    /// </summary>
    public double Strength { get; init; } = 0.6;

    /// <summary>
    /// Computes the brightness factor of every cell of a depth grid.
    /// </summary>
    /// <param name="depth">The depths, row-major (<c>row * columns + column</c>), positive down, in metres.</param>
    /// <param name="rows">The number of grid rows (row 0 is south).</param>
    /// <param name="columns">The number of grid columns (column 0 is west).</param>
    /// <param name="noDataValue">The no-data sentinel; NaN is always no-data.</param>
    /// <param name="cellWidthMetres">The east–west cell spacing on the ground, in metres.</param>
    /// <param name="cellHeightMetres">The north–south cell spacing on the ground, in metres.</param>
    /// <returns>A factor per cell, row-major: 1 for no change and for no-data cells.</returns>
    public float[] Compute(
        ReadOnlySpan<float> depth,
        int rows,
        int columns,
        float noDataValue,
        double cellWidthMetres,
        double cellHeightMetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidthMetres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeightMetres);
        if (depth.Length != rows * columns)
            throw new ArgumentException($"Expected {rows * columns} depths, got {depth.Length}.", nameof(depth));

        var factors = new float[depth.Length];
        if (factors.Length == 0)
            return factors;

        double azimuth = AzimuthDegrees * Math.PI / 180.0;
        double altitude = Math.Clamp(AltitudeDegrees, 0.0, 90.0) * Math.PI / 180.0;
        double lightEast = Math.Sin(azimuth) * Math.Cos(altitude);
        double lightNorth = Math.Cos(azimuth) * Math.Cos(altitude);
        double lightUp = Math.Sin(altitude);
        // The shade of flat ground, so flat cells keep their band colour.
        double flat = Math.Max(lightUp, 1e-6);
        double strength = Math.Clamp(Strength, 0.0, 1.0);
        // Elevation is the negated depth; fold the sign and the exaggeration
        // into the Horn denominators.
        double scaleX = -VerticalExaggeration / (8.0 * cellWidthMetres);
        double scaleY = -VerticalExaggeration / (8.0 * cellHeightMetres);
        bool noDataIsNaN = float.IsNaN(noDataValue);

        for (int r = 0; r < rows; r++)
        {
            int rs = r > 0 ? r - 1 : r;           // south neighbour row
            int rn = r < rows - 1 ? r + 1 : r;    // north neighbour row
            for (int c = 0; c < columns; c++)
            {
                int index = r * columns + c;
                float centre = depth[index];
                if (IsNoData(centre, noDataValue, noDataIsNaN))
                {
                    factors[index] = 1f;
                    continue;
                }

                int cw = c > 0 ? c - 1 : c;
                int ce = c < columns - 1 ? c + 1 : c;

                double sw = At(depth, rs, cw, columns, centre, noDataValue, noDataIsNaN);
                double s = At(depth, rs, c, columns, centre, noDataValue, noDataIsNaN);
                double se = At(depth, rs, ce, columns, centre, noDataValue, noDataIsNaN);
                double w = At(depth, r, cw, columns, centre, noDataValue, noDataIsNaN);
                double e = At(depth, r, ce, columns, centre, noDataValue, noDataIsNaN);
                double nw = At(depth, rn, cw, columns, centre, noDataValue, noDataIsNaN);
                double n = At(depth, rn, c, columns, centre, noDataValue, noDataIsNaN);
                double ne = At(depth, rn, ce, columns, centre, noDataValue, noDataIsNaN);

                // Horn: elevation gradient towards east and north.
                double dzdx = ((ne + 2 * e + se) - (nw + 2 * w + sw)) * scaleX;
                double dzdy = ((nw + 2 * n + ne) - (sw + 2 * s + se)) * scaleY;

                // Surface normal (-dz/dx, -dz/dy, 1), normalised, dotted with the light.
                double shade = (-dzdx * lightEast - dzdy * lightNorth + lightUp)
                    / Math.Sqrt(dzdx * dzdx + dzdy * dzdy + 1.0);
                if (shade < 0) shade = 0;

                factors[index] = (float)(1.0 + strength * (shade / flat - 1.0));
            }
        }

        return factors;
    }

    /// <summary>
    /// Applies a brightness factor to one colour channel: below 1 darkens
    /// towards black, above 1 lightens towards white.
    /// </summary>
    /// <param name="channel">The channel value, 0–255.</param>
    /// <param name="factor">The factor from <see cref="Compute"/>.</param>
    /// <returns>The shaded channel value.</returns>
    public static byte ShadeChannel(byte channel, float factor)
    {
        float value = factor <= 1f
            ? channel * Math.Max(factor, 0f)
            : channel + (255 - channel) * Math.Min(factor - 1f, 1f);
        return (byte)Math.Clamp(MathF.Round(value), 0f, 255f);
    }

    /// <summary>
    /// Applies a brightness factor to an opaque or premultiplied RGBA8888
    /// pixel packed little-endian (R in the low byte), leaving alpha unchanged.
    /// Lightening works in straight (unpremultiplied) colour, so a translucent
    /// pixel is lightened towards its own alpha rather than past it.
    /// </summary>
    /// <param name="packed">The packed pixel.</param>
    /// <param name="factor">The factor from <see cref="Compute"/>.</param>
    /// <returns>The shaded packed pixel.</returns>
    public static uint ShadeRgba8888(uint packed, float factor)
    {
        if (factor == 1f)
            return packed;
        byte a = (byte)(packed >> 24);
        if (a == 0)
            return packed;
        byte r = (byte)packed, g = (byte)(packed >> 8), b = (byte)(packed >> 16);
        if (a != 255)
        {
            // Premultiplied → straight, shade, → premultiplied.
            r = (byte)Math.Min(255, r * 255 / a);
            g = (byte)Math.Min(255, g * 255 / a);
            b = (byte)Math.Min(255, b * 255 / a);
            r = (byte)(ShadeChannel(r, factor) * a / 255);
            g = (byte)(ShadeChannel(g, factor) * a / 255);
            b = (byte)(ShadeChannel(b, factor) * a / 255);
        }
        else
        {
            r = ShadeChannel(r, factor);
            g = ShadeChannel(g, factor);
            b = ShadeChannel(b, factor);
        }

        return (uint)(r | (g << 8) | (b << 16) | (a << 24));
    }

    private static bool IsNoData(float value, float noDataValue, bool noDataIsNaN) =>
        float.IsNaN(value) || (!noDataIsNaN && value == noDataValue);

    private static double At(
        ReadOnlySpan<float> depth, int row, int column, int columns,
        float centre, float noDataValue, bool noDataIsNaN)
    {
        float value = depth[row * columns + column];
        return IsNoData(value, noDataValue, noDataIsNaN) ? centre : value;
    }
}
