namespace EncDotNet.S100.Tides;

/// <summary>
/// Predicts the astronomical tide at a station from its harmonic
/// constituents: the height at a time, a series at a fixed step, and the
/// high and low waters in a period.
/// </summary>
/// <remarks>
/// <para>
/// The height is h(t) = Z₀ + Σ f·H·cos(V₀ + u + σt − G), the prediction
/// formula of Schureman, <i>Manual of Harmonic Analysis and Prediction of
/// Tides</i> (US Coast and Geodetic Survey Special Publication 98, § 31).
/// As in NOAA's practice, V₀ is taken at the start of each calendar year
/// (UTC) and the nodal terms f and u at its middle, so a year's prediction
/// is a plain sum of cosines.
/// </para>
/// <para>
/// A prediction is astronomical only: it leaves out the weather, river flow
/// and other non-tidal effects on the water level. Times are UTC and heights
/// are metres above the station's <see cref="TideStation.Datum"/>. A
/// <see cref="DateTime"/> of <see cref="DateTimeKind.Unspecified"/> kind is
/// taken as UTC.
/// </para>
/// <para>
/// An instance is immutable apart from an internal cache and is safe to use
/// from several threads at once.
/// </para>
/// </remarks>
public sealed class TidePredictor
{
    /// <summary>
    /// Phasor series re-seed from the exact phases at least this often, so
    /// rounding in the rotation can't build up.
    /// </summary>
    private const int ReseedInterval = 4096;

    /// <summary>The longest step of the derivative scan for highs and lows, in hours.</summary>
    private const double MaximumScanStepHours = 0.25;

    /// <summary>
    /// A high and a low closer in height than this are a stand, not a turn
    /// of the tide, and aren't reported: a millimetre is the precision NOAA
    /// publishes heights to, and the smallest turn NOAA's own predictions
    /// list is a few millimetres.
    /// </summary>
    public const double MinimumTurnMeters = 0.001;

    private readonly ConstituentDefinition[] _definitions;
    private readonly double[] _amplitudes;
    private readonly double[] _greenwichPhases;
    private readonly double[] _speeds;
    private readonly double _mean;
    private readonly double _scanStepHours;
    private volatile YearTerms? _cache;

    /// <summary>Creates a predictor for a station.</summary>
    /// <param name="station">The station.</param>
    public TidePredictor(TideStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        Station = station;

        var count = station.Constituents.Count;
        _definitions = new ConstituentDefinition[count];
        _amplitudes = new double[count];
        _greenwichPhases = new double[count];
        _speeds = new double[count];
        var fastest = 0.0;
        for (var i = 0; i < count; i++)
        {
            var constituent = station.Constituents[i];
            _definitions[i] = ConstituentTable.All[ConstituentTable.IndexOf(constituent.Name)];
            _amplitudes[i] = constituent.AmplitudeMeters;
            _greenwichPhases[i] = constituent.PhaseDegrees * Angles.DegreesToRadians;
            _speeds[i] = _definitions[i].Speed * Angles.DegreesToRadians;
            if (constituent.AmplitudeMeters != 0)
            {
                fastest = Math.Max(fastest, _speeds[i]);
            }
        }

        _mean = station.Datum.MeanSeaLevelAboveDatumMeters;

        // Twelve scan steps to the period of the fastest constituent present
        // (so at least six between two of its turns), and no more than 15
        // minutes.
        _scanStepHours = fastest > 0
            ? Math.Min(MaximumScanStepHours, 2 * Math.PI / fastest / 12)
            : MaximumScanStepHours;
    }

    /// <summary>The station the predictions are for.</summary>
    public TideStation Station { get; }

    /// <summary>The predicted height at a time.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The height above the station's datum, in metres.</returns>
    public double HeightAt(DateTime utc)
    {
        var time = ToUtc(utc, nameof(utc));
        var terms = TermsFor(time.Year);
        var hours = HoursSince(terms.Start, time);
        var height = _mean;
        for (var i = 0; i < _amplitudes.Length; i++)
        {
            height += terms.Amplitudes[i] * Math.Cos(terms.Phases[i] + (_speeds[i] * hours));
        }

        return height;
    }

    /// <summary>The predicted rate of rise at a time: positive while the tide rises.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The rate, in metres per hour.</returns>
    public double RateAt(DateTime utc)
    {
        var time = ToUtc(utc, nameof(utc));
        var terms = TermsFor(time.Year);
        var (rate, _) = Derivatives(terms, HoursSince(terms.Start, time));
        return rate;
    }

    /// <summary>
    /// Predicts a series of heights at a fixed step, without allocating per
    /// sample: sample <c>k</c> is the height at
    /// <paramref name="startUtc"/> + <c>k</c> × <paramref name="step"/>.
    /// </summary>
    /// <param name="startUtc">The time of the first sample (UTC).</param>
    /// <param name="step">The time between samples; must be positive.</param>
    /// <param name="heights">
    /// Receives the heights above the station's datum, in metres, one per
    /// element.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> isn't positive.</exception>
    public void Predict(DateTime startUtc, TimeSpan step, Span<double> heights)
    {
        var start = ToUtc(startUtc, nameof(startUtc));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        Sweep(start, step, heights, derivative: false);
    }

    /// <summary>
    /// Finds the high and low waters between two times. Each is a root of
    /// the rate of rise, bracketed by a scan of the rate and refined to
    /// under a second, so its time doesn't depend on a sampling step.
    /// </summary>
    /// <param name="startUtc">The start of the period (UTC).</param>
    /// <param name="endUtc">The end of the period (UTC); must not be before the start.</param>
    /// <returns>
    /// The highs and lows strictly inside the period, in time order, high
    /// and low alternating. A high and low that differ by less than
    /// <see cref="MinimumTurnMeters"/> in height are a stand and are left out
    /// together.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="endUtc"/> is before <paramref name="startUtc"/>.</exception>
    public IReadOnlyList<TideExtreme> FindExtremes(DateTime startUtc, DateTime endUtc)
    {
        var start = ToUtc(startUtc, nameof(startUtc));
        var end = ToUtc(endUtc, nameof(endUtc));
        if (end < start)
        {
            throw new ArgumentException("The end of the period is before its start.", nameof(endUtc));
        }

        var step = TimeSpan.FromHours(_scanStepHours);
        var count = (int)Math.Ceiling((end - start) / step) + 1;
        var extremes = new List<TideExtreme>();
        const int chunk = 1024;
        Span<double> rates = stackalloc double[chunk + 1];

        // Scan the rate in chunks; each chunk repeats the previous chunk's
        // last sample so a sign change across the seam isn't missed.
        for (var first = 0; first < count - 1; first += chunk)
        {
            var length = Math.Min(chunk, count - 1 - first) + 1;
            var chunkStart = start + (step * first);
            var window = rates[..length];
            Sweep(chunkStart, step, window, derivative: true);
            for (var k = 0; k + 1 < length; k++)
            {
                var a = window[k];
                var b = window[k + 1];
                if (a > 0 && b <= 0 || a < 0 && b >= 0)
                {
                    // A rate of exactly zero at a sample belongs to the
                    // bracket it ends, not the one it starts.
                    // The last bracket may run past the end of the period;
                    // a root found there is dropped below.
                    var left = chunkStart + (step * k);
                    var time = Refine(left, left + step, a);
                    if (time > start && time < end)
                    {
                        var kind = a > 0 ? TideExtremeKind.High : TideExtremeKind.Low;
                        var extreme = new TideExtreme(time, HeightAt(time), kind);
                        if (extremes.Count > 0
                            && extremes[^1].Kind != kind
                            && Math.Abs(extremes[^1].HeightMeters - extreme.HeightMeters) < MinimumTurnMeters)
                        {
                            // A stand: drop the turn it ends along with it.
                            extremes.RemoveAt(extremes.Count - 1);
                        }
                        else
                        {
                            extremes.Add(extreme);
                        }
                    }
                }
            }
        }

        return extremes;
    }

    /// <summary>
    /// Fills <paramref name="values"/> with heights, or rates of rise, at a
    /// step. Each constituent is a phasor turned by its speed times the
    /// step, so a sample costs a complex multiply per constituent rather
    /// than a cosine.
    /// </summary>
    private void Sweep(DateTime start, TimeSpan step, Span<double> values, bool derivative)
    {
        var n = _amplitudes.Length;
        Span<double> scratch = stackalloc double[4 * n];
        var real = scratch[..n];
        var imaginary = scratch.Slice(n, n);
        var cosStep = scratch.Slice(2 * n, n);
        var sinStep = scratch.Slice(3 * n, n);
        var stepHours = step.Ticks / (double)TimeSpan.TicksPerHour;
        for (var i = 0; i < n; i++)
        {
            cosStep[i] = Math.Cos(_speeds[i] * stepHours);
            sinStep[i] = Math.Sin(_speeds[i] * stepHours);
        }

        var k = 0;
        while (k < values.Length)
        {
            // Seed a segment from the exact phases. A segment ends at the
            // next calendar year, when the yearly terms change, or after
            // ReseedInterval samples.
            var time = start + (step * k);
            var terms = TermsFor(time.Year);
            var nextYear = terms.Start.AddYears(1);
            var segment = (int)Math.Min(
                Math.Min(values.Length - k, ReseedInterval),
                Math.Ceiling((nextYear - time) / step));
            var hours = HoursSince(terms.Start, time);
            for (var i = 0; i < n; i++)
            {
                var phase = terms.Phases[i] + (_speeds[i] * hours);
                var amplitude = derivative ? terms.Amplitudes[i] * _speeds[i] : terms.Amplitudes[i];
                real[i] = amplitude * Math.Cos(phase);
                imaginary[i] = amplitude * Math.Sin(phase);
            }

            var end = k + segment;
            for (; k < end; k++)
            {
                var sum = 0.0;
                for (var i = 0; i < n; i++)
                {
                    var re = real[i];
                    var im = imaginary[i];
                    sum += derivative ? im : re;
                    real[i] = (re * cosStep[i]) - (im * sinStep[i]);
                    imaginary[i] = (re * sinStep[i]) + (im * cosStep[i]);
                }

                // d/dt A·cos(φ) = −A·σ·sin(φ).
                values[k] = derivative ? -sum : _mean + sum;
            }
        }
    }

    /// <summary>
    /// Finds the root of the rate of rise between two times where it changes
    /// sign, by Newton's method kept inside a shrinking bracket.
    /// </summary>
    private DateTime Refine(DateTime left, DateTime right, double leftRate)
    {
        var terms = TermsFor(left.Year);
        if (right.Year != left.Year)
        {
            // A bracket across New Year: keep to one year's terms by
            // refining in the year that holds most of it.
            var newYear = new DateTime(right.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (right - newYear > newYear - left)
            {
                terms = TermsFor(right.Year);
            }
        }

        var a = HoursSince(terms.Start, left);
        var b = HoursSince(terms.Start, right);
        var rising = leftRate > 0;
        var x = (a + b) / 2;
        const double tolerance = 0.1 / 3600;
        for (var iteration = 0; iteration < 60 && b - a > tolerance; iteration++)
        {
            var (rate, acceleration) = Derivatives(terms, x);
            if (rate == 0)
            {
                break;
            }

            // Shrink the bracket: the root is on the side where the rate
            // has the sign opposite to the left end's.
            if (rate > 0 == rising)
            {
                a = x;
            }
            else
            {
                b = x;
            }

            var next = acceleration != 0 ? x - (rate / acceleration) : double.NaN;
            x = next > a && next < b ? next : (a + b) / 2;
        }

        return terms.Start.AddTicks((long)Math.Round(x * TimeSpan.TicksPerHour));
    }

    /// <summary>The first and second time derivatives of the height, in m/h and m/h².</summary>
    private (double Rate, double Acceleration) Derivatives(YearTerms terms, double hours)
    {
        var rate = 0.0;
        var acceleration = 0.0;
        for (var i = 0; i < _amplitudes.Length; i++)
        {
            var (sin, cos) = Math.SinCos(terms.Phases[i] + (_speeds[i] * hours));
            var a = terms.Amplitudes[i] * _speeds[i];
            rate -= a * sin;
            acceleration -= a * _speeds[i] * cos;
        }

        return (rate, acceleration);
    }

    /// <summary>The year's amplitudes f·H and phases V₀ + u − G, cached for the last year asked for.</summary>
    private YearTerms TermsFor(int year)
    {
        var cached = _cache;
        if (cached is not null && cached.Year == year)
        {
            return cached;
        }

        var terms = YearTerms.Create(year, _definitions, _amplitudes, _greenwichPhases);
        _cache = terms;
        return terms;
    }

    private static double HoursSince(DateTime origin, DateTime time) =>
        (time - origin).Ticks / (double)TimeSpan.TicksPerHour;

    private static DateTime ToUtc(DateTime time, string parameterName) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        _ => throw new ArgumentException("The time must be UTC, not local.", parameterName),
    };

    /// <summary>One calendar year's prediction terms.</summary>
    private sealed class YearTerms
    {
        private YearTerms(int year, double[] amplitudes, double[] phases)
        {
            Year = year;
            Start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Amplitudes = amplitudes;
            Phases = phases;
        }

        public int Year { get; }

        /// <summary>Midnight UTC at the start of the year, the origin of the phases.</summary>
        public DateTime Start { get; }

        /// <summary>f·H per constituent, in metres.</summary>
        public double[] Amplitudes { get; }

        /// <summary>V₀ + u − G per constituent, in radians, at <see cref="Start"/>.</summary>
        public double[] Phases { get; }

        public static YearTerms Create(
            int year, ConstituentDefinition[] definitions, double[] amplitudes, double[] greenwichPhases)
        {
            var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var middle = start + ((start.AddYears(1) - start) / 2);
            Span<double> elements = stackalloc double[6];
            Astronomy.Elements(start, elements);
            var nodal = Astronomy.Nodal(middle);

            var f = new double[definitions.Length];
            var phases = new double[definitions.Length];
            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                f[i] = amplitudes[i] * definition.NodeFactorValue(nodal);
                var degrees = definition.EquilibriumArgument(elements) + definition.NodalCorrection(nodal);
                phases[i] = (Angles.Normalize(degrees) * Angles.DegreesToRadians) - greenwichPhases[i];
            }

            return new YearTerms(year, f, phases);
        }
    }
}
