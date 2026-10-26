namespace EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

/// <summary>A resolution level's bounds in tile-component coordinates (T.800 B-14).</summary>
internal readonly record struct ResolutionBounds(int X0, int Y0, int X1, int Y1)
{
    public int Width => X1 - X0;

    public int Height => Y1 - Y0;
}

/// <summary>
/// The inverse discrete wavelet transform (T.800 Annex F): 2D_SR, level by
/// level, each a horizontal then a vertical 1D_SR, by lifting.
/// </summary>
/// <remarks>
/// <para>
/// A tile-component's coefficients sit in one buffer, each resolution's
/// region in its top-left corner: the low-pass half of a row (or column)
/// first, then the high-pass half. Each 1D step interleaves a line into a
/// scratch line, applying the periodic symmetric extension (F.3.7) beyond
/// its ends, lifts it, and writes it back in natural order.
/// </para>
/// <para>
/// A line's first coordinate decides its parity: even coordinates hold
/// low-pass samples. A line of one sample is copied, or halved if its
/// coordinate is odd (F.3.7).
/// </para>
/// </remarks>
internal static class InverseWavelet
{
    // Extension on each side. Each lifting step leaves one more sample at
    // each end unusable, so the 9/7 filter's four steps need five; six keeps
    // the buffer's parity equal to the line's.
    private const int Pad = 6;

    // Table F.4: the lifting parameters and K of the 9/7 filter.
    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float K = 1.230174104914001f;

    /// <summary>Reconstructs the 5/3 reversible transform in place.</summary>
    /// <param name="data">The tile-component's coefficients, <paramref name="stride"/> across.</param>
    /// <param name="stride">The buffer's row length.</param>
    /// <param name="resolutions">The bounds of resolutions 0 to N_L.</param>
    public static void Reversible(int[] data, int stride, ResolutionBounds[] resolutions)
    {
        var longest = 0;
        foreach (var r in resolutions)
            longest = Math.Max(longest, Math.Max(r.Width, r.Height));
        var line = new int[longest + (2 * Pad)];
        var column = new int[longest];

        for (var level = 1; level < resolutions.Length; level++)
        {
            var r = resolutions[level];
            int w = r.Width, h = r.Height;
            if (w == 0 || h == 0)
                continue;

            for (var y = 0; y < h; y++)
                Lift53(data.AsSpan(y * stride, w), r.X0, line);

            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                    column[y] = data[(y * stride) + x];
                Lift53(column.AsSpan(0, h), r.Y0, line);
                for (var y = 0; y < h; y++)
                    data[(y * stride) + x] = column[y];
            }
        }
    }

    /// <summary>Reconstructs the 9/7 irreversible transform in place.</summary>
    /// <param name="data">The tile-component's dequantised coefficients, <paramref name="stride"/> across.</param>
    /// <param name="stride">The buffer's row length.</param>
    /// <param name="resolutions">The bounds of resolutions 0 to N_L.</param>
    public static void Irreversible(float[] data, int stride, ResolutionBounds[] resolutions)
    {
        var longest = 0;
        foreach (var r in resolutions)
            longest = Math.Max(longest, Math.Max(r.Width, r.Height));
        var line = new float[longest + (2 * Pad)];
        var column = new float[longest];

        for (var level = 1; level < resolutions.Length; level++)
        {
            var r = resolutions[level];
            int w = r.Width, h = r.Height;
            if (w == 0 || h == 0)
                continue;

            for (var y = 0; y < h; y++)
                Lift97(data.AsSpan(y * stride, w), r.X0, line);

            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                    column[y] = data[(y * stride) + x];
                Lift97(column.AsSpan(0, h), r.Y0, line);
                for (var y = 0; y < h; y++)
                    data[(y * stride) + x] = column[y];
            }
        }
    }

    // 1D_SR for the 5/3 filter (F-5, F-6) on one line, low-pass half first.
    internal static void Lift53(Span<int> samples, int i0, int[] line)
    {
        var n = samples.Length;
        if (n == 1)
        {
            if ((i0 & 1) != 0)
                samples[0] /= 2;
            return;
        }

        Interleave(samples, i0, line);
        var end = n + (2 * Pad);

        // The buffer's index t holds coordinate i0 − Pad + t, so it's even
        // (low-pass) when t has the parity of i0 − Pad, that is of i0.
        var even = i0 & 1;
        for (var t = even == 0 ? 2 : 1; t < end - 1; t += 2)
            line[t] -= (line[t - 1] + line[t + 1] + 2) >> 2;
        for (var t = even == 0 ? 3 : 2; t < end - 1; t += 2)
            line[t] += (line[t - 1] + line[t + 1]) >> 1;

        line.AsSpan(Pad, n).CopyTo(samples);
    }

    // 1D_SR for the 9/7 filter (F-7) on one line, low-pass half first.
    internal static void Lift97(Span<float> samples, int i0, float[] line)
    {
        var n = samples.Length;
        if (n == 1)
        {
            if ((i0 & 1) != 0)
                samples[0] /= 2;
            return;
        }

        Interleave(samples, i0, line);
        var end = n + (2 * Pad);
        var firstEven = (i0 & 1) == 0 ? 0 : 1;
        var firstOdd = 1 - firstEven;

        for (var t = firstEven; t < end; t += 2)
            line[t] *= K;
        for (var t = firstOdd; t < end; t += 2)
            line[t] *= 1 / K;

        // Each step starts one sample further in than the last, where its
        // neighbours have been lifted.
        var start = firstEven == 0 ? 2 : 1;
        for (var t = start; t < end - 1; t += 2)
            line[t] -= Delta * (line[t - 1] + line[t + 1]);
        for (var t = start + 1; t < end - 1; t += 2)
            line[t] -= Gamma * (line[t - 1] + line[t + 1]);
        for (var t = start + 2; t < end - 1; t += 2)
            line[t] -= Beta * (line[t - 1] + line[t + 1]);
        for (var t = start + 3; t < end - 1; t += 2)
            line[t] -= Alpha * (line[t - 1] + line[t + 1]);

        line.AsSpan(Pad, n).CopyTo(samples);
    }

    // Interleaves the low-pass and high-pass halves of a line into
    // line[Pad .. Pad + n), then extends it symmetrically by Pad samples on
    // each side (F.3.7: PSE_O, whole-sample symmetric, period 2(n − 1)).
    private static void Interleave<T>(Span<T> samples, int i0, T[] line)
    {
        var n = samples.Length;
        var lows = ((i0 + n + 1) >> 1) - ((i0 + 1) >> 1);
        int low = 0, high = lows;
        for (var k = 0; k < n; k++)
            line[Pad + k] = ((i0 + k) & 1) == 0 ? samples[low++] : samples[high++];

        var period = 2 * (n - 1);
        for (var k = 1; k <= Pad; k++)
        {
            line[Pad - k] = line[Pad + Reflect(-k, n, period)];
            line[Pad + n - 1 + k] = line[Pad + Reflect(n - 1 + k, n, period)];
        }
    }

    private static int Reflect(int k, int n, int period)
    {
        var m = ((k % period) + period) % period;
        return m < n ? m : period - m;
    }
}
