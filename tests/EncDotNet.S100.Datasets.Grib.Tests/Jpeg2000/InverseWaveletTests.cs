using EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

namespace EncDotNet.S100.Datasets.Grib.Tests.Jpeg2000;

/// <summary>
/// The 1D inverse transforms against forward transforms written here from
/// T.800 F.4 (1D_SD), for every length up to 21 and both parities of the
/// first coordinate.
/// </summary>
public class InverseWaveletTests
{
    public static TheoryData<int, int> Lines()
    {
        var data = new TheoryData<int, int>();
        for (var n = 1; n <= 21; n++)
        {
            for (var i0 = 0; i0 <= 3; i0++)
                data.Add(n, i0);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void Reversible_5_3_is_exact(int n, int i0)
    {
        var random = new Random((n * 7) + i0);
        var x = Enumerable.Range(0, n).Select(_ => random.Next(-5000, 5000)).ToArray();

        var y = Forward53(x, i0);
        InverseWavelet.Lift53(y, i0, new int[n + 12]);

        Assert.Equal(x, y);
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void Irreversible_9_7_inverts_the_forward_transform(int n, int i0)
    {
        var random = new Random((n * 11) + i0);
        var x = Enumerable.Range(0, n).Select(_ => (float)random.Next(-5000, 5000)).ToArray();

        var y = Forward97(x, i0);
        InverseWavelet.Lift97(y, i0, new float[n + 12]);

        for (var i = 0; i < n; i++)
            Assert.Equal(x[i], y[i], 0.01f);
    }

    // 1D_SD with the 5/3 filter (F-9, F-10): returns the low-pass then the high-pass samples.
    private static int[] Forward53(int[] x, int i0)
    {
        var n = x.Length;
        if (n == 1)
            return [(i0 & 1) == 0 ? x[0] : 2 * x[0]];

        var e = Extend(x, 4);
        for (var t = 1; t < e.Length - 1; t++)
        {
            if (((i0 + t - 4) & 1) != 0)
                e[t] -= (int)Math.Floor((e[t - 1] + e[t + 1]) / 2.0);
        }

        var y = (int[])e.Clone();
        for (var t = 2; t < e.Length - 2; t++)
        {
            if (((i0 + t - 4) & 1) == 0)
                y[t] = e[t] + (int)Math.Floor((e[t - 1] + e[t + 1] + 2) / 4.0);
        }

        return Deinterleave(y.AsSpan(4, n).ToArray(), i0);
    }

    // 1D_SD with the 9/7 filter (F-11).
    private static float[] Forward97(float[] x, int i0)
    {
        const float alpha = -1.586134342059924f, beta = -0.052980118572961f, gamma = 0.882911075530934f, delta = 0.443506852043971f, k = 1.230174104914001f;
        var n = x.Length;
        if (n == 1)
            return [(i0 & 1) == 0 ? x[0] : 2 * x[0]];

        var e = Extend(x, 6);
        bool Odd(int t) => ((i0 + t - 6) & 1) != 0;
        void Step(bool odd, float c, int margin)
        {
            var copy = (float[])e.Clone();
            for (var t = margin; t < e.Length - margin; t++)
            {
                if (Odd(t) == odd)
                    e[t] = copy[t] + (c * (copy[t - 1] + copy[t + 1]));
            }
        }

        Step(true, alpha, 1);
        Step(false, beta, 2);
        Step(true, gamma, 3);
        Step(false, delta, 4);
        for (var t = 0; t < e.Length; t++)
            e[t] = Odd(t) ? e[t] * k : e[t] / k;

        return Deinterleave(e.AsSpan(6, n).ToArray(), i0);
    }

    private static T[] Extend<T>(T[] x, int pad)
    {
        var n = x.Length;
        var period = 2 * (n - 1);
        var e = new T[n + (2 * pad)];
        for (var t = 0; t < e.Length; t++)
        {
            var m = (((t - pad) % period) + period) % period;
            e[t] = x[m < n ? m : period - m];
        }

        return e;
    }

    private static T[] Deinterleave<T>(T[] interleaved, int i0)
    {
        var lows = interleaved.Where((_, k) => ((i0 + k) & 1) == 0);
        var highs = interleaved.Where((_, k) => ((i0 + k) & 1) != 0);
        return [.. lows, .. highs];
    }
}
