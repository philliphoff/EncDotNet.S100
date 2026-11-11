using System.Globalization;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>
/// Reads NDBC's <c>realtime2/{station}.txt</c> standard meteorological format:
/// the last 45 days of observations, newest first.
/// </summary>
/// <remarks>
/// <para>The file starts with two comment lines, the column names and their units:</para>
/// <code>
/// #YY  MM DD hh mm WDIR WSPD GST  WVHT   DPD   APD MWD   PRES  ATMP  WTMP  DEWP  VIS PTDY  TIDE
/// #yr  mo dy hr mn degT m/s  m/s     m   sec   sec degT   hPa  degC  degC  degC  nmi  hPa    ft
/// 2026 10 27 14 50 230  7.0  9.0   1.1     6   4.6 210 1016.2  11.8  13.1   6.4   MM -0.8    MM
/// </code>
/// <para>
/// Columns are found by name, so a reordered or extended header still reads,
/// and the historical names (<c>WD</c>, <c>BAR</c>, <c>YYYY</c>) are accepted.
/// <c>MM</c> marks a missing value. A row with the wrong number of fields (such
/// as the last, cut-off line of a ranged download) or a bad time is skipped.
/// </para>
/// </remarks>
public static class NdbcRealtimeParser
{
    /// <summary>The <c>MM</c> token NDBC writes for a missing value.</summary>
    public const string Missing = "MM";

    /// <summary>Reads the observations in <paramref name="text"/>.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="stationId">The station the file belongs to.</param>
    /// <returns>The rows, in file order (newest first).</returns>
    /// <exception cref="FormatException">The text has no <c>#YY</c> header line.</exception>
    public static IReadOnlyList<NdbcObservation> Parse(string text, string stationId)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return Parse(reader, stationId);
    }

    /// <summary>Reads observations from <paramref name="reader"/>.</summary>
    /// <param name="reader">The file.</param>
    /// <param name="stationId">The station the file belongs to.</param>
    /// <param name="notBefore">When set, reading stops at the first row older than this time, so only the head of a long file is read.</param>
    /// <returns>The rows, in file order (newest first).</returns>
    /// <exception cref="FormatException">The text has no <c>#YY</c> header line before its first row.</exception>
    public static IReadOnlyList<NdbcObservation> Parse(TextReader reader, string stationId, DateTimeOffset? notBefore = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        Columns? columns = null;
        var rows = new List<NdbcObservation>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var span = line.AsSpan().Trim();
            if (span.IsEmpty)
                continue;

            if (span[0] == '#')
            {
                // The first comment line names the columns; the second gives units.
                if (columns is null && IsHeader(span))
                    columns = Columns.From(Split(span.TrimStart('#').ToString()));
                continue;
            }

            if (columns is null)
                throw new FormatException($"The NDBC file for station {stationId} has no '#YY' header line.");

            var fields = Split(span.ToString());
            if (fields.Length != columns.Count || !columns.TryReadTime(fields, out var time))
                continue;

            if (notBefore is { } limit && time < limit)
                break;

            rows.Add(columns.Read(fields, stationId, time));
        }

        if (columns is null)
            throw new FormatException($"The NDBC file for station {stationId} has no '#YY' header line.");

        return rows;
    }

    private static bool IsHeader(ReadOnlySpan<char> line)
    {
        var first = line.TrimStart('#').TrimStart();
        return first.StartsWith("YY", StringComparison.Ordinal) || first.StartsWith("YYYY", StringComparison.Ordinal);
    }

    private static string[] Split(string line) =>
        line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private sealed class Columns
    {
        private readonly Dictionary<string, int> _index;

        private Columns(Dictionary<string, int> index, int count)
        {
            _index = index;
            Count = count;
        }

        public int Count { get; }

        public static Columns From(string[] names)
        {
            // Ordinal: "MM" is the month and "mm" the minute.
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i] switch
                {
                    "YYYY" or "YY" => "YY",
                    "WD" => "WDIR",
                    "BAR" => "PRES",
                    var other => other,
                };
                index.TryAdd(name, i);
            }

            foreach (var required in (string[])["YY", "MM", "DD", "hh"])
            {
                if (!index.ContainsKey(required))
                    throw new FormatException($"The NDBC header has no '{required}' column.");
            }

            return new Columns(index, names.Length);
        }

        public bool TryReadTime(string[] fields, out DateTimeOffset time)
        {
            time = default;
            if (!TryInt(fields, "YY", out int year) || !TryInt(fields, "MM", out int month)
                || !TryInt(fields, "DD", out int day) || !TryInt(fields, "hh", out int hour))
            {
                return false;
            }

            int minute = TryInt(fields, "mm", out int m) ? m : 0;
            if (year < 100)
                year += year < 70 ? 2000 : 1900;

            try
            {
                time = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public NdbcObservation Read(string[] fields, string stationId, DateTimeOffset time) => new()
        {
            StationId = stationId,
            Time = time,
            WindFromDirection = Value(fields, "WDIR") is { } wdir ? Angle.FromDegrees(wdir) : null,
            WindSpeed = Value(fields, "WSPD") is { } wspd ? Speed.FromMetresPerSecond(wspd) : null,
            WindGust = Value(fields, "GST") is { } gst ? Speed.FromMetresPerSecond(gst) : null,
            WaveHeight = Value(fields, "WVHT") is { } wvht ? Length.FromMetres(wvht) : null,
            DominantWavePeriodSeconds = Value(fields, "DPD"),
            AverageWavePeriodSeconds = Value(fields, "APD"),
            MeanWaveDirection = Value(fields, "MWD") is { } mwd ? Angle.FromDegrees(mwd) : null,
            PressureHectopascals = Value(fields, "PRES"),
            AirTemperatureCelsius = Value(fields, "ATMP"),
            WaterTemperatureCelsius = Value(fields, "WTMP"),
            DewPointCelsius = Value(fields, "DEWP"),
            VisibilityNauticalMiles = Value(fields, "VIS"),
            PressureTendencyHectopascals = Value(fields, "PTDY"),
            TideFeet = Value(fields, "TIDE"),
        };

        private double? Value(string[] fields, string column)
        {
            if (!_index.TryGetValue(column, out int i))
                return null;
            string token = fields[i];
            if (token == Missing)
                return null;
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && double.IsFinite(value)
                ? value
                : null;
        }

        private bool TryInt(string[] fields, string column, out int value)
        {
            value = 0;
            return _index.TryGetValue(column, out int i)
                && int.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}
