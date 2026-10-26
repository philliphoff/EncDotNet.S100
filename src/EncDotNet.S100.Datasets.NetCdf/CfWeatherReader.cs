using System.Globalization;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// The CF interpretation behind <see cref="NetCdfWeatherDataset"/>: finds the
/// coordinates, checks the grid, maps variables to the weather parameter
/// table and builds lazily decoded time steps. See
/// <see cref="NetCdfWeatherDataset"/> for the rules.
/// </summary>
internal static class CfWeatherReader
{
    // Relative tolerance for "evenly spaced" coordinates: float32 coordinate
    // values carry about 7 significant digits.
    private const double SpacingTolerance = 1e-3;

    private static readonly string[] LatitudeUnits = ["degrees_north", "degree_north", "degree_n", "degrees_n", "degreen", "degreesn"];
    private static readonly string[] LongitudeUnits = ["degrees_east", "degree_east", "degree_e", "degrees_e", "degreee", "degreese"];

    public sealed record Result(
        WeatherGridSet GridSet,
        IReadOnlyList<DateTime> Times,
        DateTime? ReferenceTime,
        IReadOnlyList<NetCdfIgnoredVariable> Ignored);

    public static Result Read(NetCdfFile file, string? name)
    {
        var latitude = FindAxis(file, Axis.Latitude);
        var longitude = FindAxis(file, Axis.Longitude);
        if (latitude is null || longitude is null)
        {
            throw new CfConventionsException(
                $"The file has no {(latitude is null ? "latitude" : "longitude")} coordinate this reader recognises " +
                "(a variable with standard_name 'latitude'/'longitude', axis 'Y'/'X', or units 'degrees_north'/'degrees_east'), " +
                $"so it isn't a CF latitude/longitude grid. Variables found: {ListVariables(file)}.");
        }

        RequireOneDimensional(latitude, longitude);
        var time = FindTime(file);
        var times = time is null ? [] : ReadTimes(file, time);
        if (time is not null && times.Count == 0)
            throw new CfConventionsException($"The time coordinate '{time.Name}' has no values, so the fields have no valid time.");
        var referenceTime = FindReferenceTime(file);

        var latitudes = ReadCoordinate(file, latitude);
        var longitudes = ReadCoordinate(file, longitude);
        var latitudeIncrement = RegularIncrement(latitude, latitudes);
        var longitudeIncrement = RegularIncrement(longitude, longitudes);
        var native = new WeatherNativeGrid(
            latitudes.Length,
            longitudes.Length,
            latitudes[0],
            longitudes[0],
            latitudeIncrement,
            longitudeIncrement);

        var latitudeDimension = latitude.Dimensions[0];
        var longitudeDimension = longitude.Dimensions[0];
        var timeDimension = time?.Dimensions.Count == 1 ? time.Dimensions[0] : null;
        var coordinateNames = new HashSet<string>(StringComparer.Ordinal) { latitude.Name, longitude.Name };
        if (time is not null)
            coordinateNames.Add(time.Name);
        foreach (var variable in file.Variables)
        {
            // Cell bounds and auxiliary variables aren't data.
            if (variable.GetText("bounds") is { } bounds)
                coordinateNames.Add(bounds);
        }

        var candidates = new List<Candidate>();
        var ignored = new List<NetCdfIgnoredVariable>();
        foreach (var variable in file.Variables)
        {
            if (coordinateNames.Contains(variable.Name) || variable.IsCoordinateVariable || !variable.IsNumeric)
                continue;

            int latitudeIndex = IndexOf(variable, latitudeDimension);
            int longitudeIndex = IndexOf(variable, longitudeDimension);
            if (latitudeIndex < 0 || longitudeIndex < 0)
            {
                // Scalars and auxiliary variables (crs, forecast_reference_time) aren't on the grid.
                if (variable.Dimensions.Count >= 2)
                    ignored.Add(new(variable.Name, $"isn't on the {latitude.Name} × {longitude.Name} grid"));
                continue;
            }

            RequireLatitudeLongitudeMapping(file, variable);

            int rank = variable.Dimensions.Count;
            if (Math.Max(latitudeIndex, longitudeIndex) != rank - 1 || Math.Min(latitudeIndex, longitudeIndex) != rank - 2)
            {
                ignored.Add(new(variable.Name, $"doesn't end with its {latitude.Name} and {longitude.Name} dimensions"));
                continue;
            }

            int timeIndex = timeDimension is null ? -1 : IndexOf(variable, timeDimension);
            string? extra = null;
            for (int d = 0; d < rank - 2; d++)
            {
                if (d != timeIndex && variable.Dimensions[d].Length != 1)
                {
                    extra = string.Create(CultureInfo.InvariantCulture,
                        $"has {variable.Dimensions[d].Length} levels along '{variable.Dimensions[d].Name}'; only single-level variables are read");
                    break;
                }
            }

            if (extra is not null)
            {
                ignored.Add(new(variable.Name, extra));
                continue;
            }

            if (timeIndex < 0 && times.Count > 1)
            {
                ignored.Add(new(variable.Name, $"has no '{time!.Name}' dimension in a file with {times.Count} time steps"));
                continue;
            }

            candidates.Add(new Candidate(variable, timeIndex, longitudeIndex < latitudeIndex));
        }

        if (time is null && candidates.Count > 0)
        {
            throw new CfConventionsException(
                $"The file has no time coordinate this reader recognises (standard_name 'time', axis 'T', or units '<unit> since <time>'), so its fields have no valid time. Variables found: {ListVariables(file)}.");
        }

        var parameters = BuildParameters(file, candidates, times, native, ignored);
        if (!parameters.Any(p => p.IsKnown))
        {
            throw new CfConventionsException(
                "No variable on the grid has a CF standard_name the weather parameter table knows " +
                $"({string.Join(", ", WeatherParameters.All.SelectMany(d => d.CfStandardNames).Distinct())}). " +
                $"Variables found: {ListVariables(file)}.");
        }

        string model = file.GetGlobalText("source")
            ?? file.GetGlobalText("institution")
            ?? file.GetGlobalText("title")
            ?? (string.IsNullOrWhiteSpace(name) ? "NetCDF" : name);
        var origin = new WeatherOrigin(model, referenceTime ?? times.Min(), file.GetGlobalText("institution"));
        return new Result(new WeatherGridSet(origin, native, parameters), times, referenceTime, ignored);
    }

    // ---------------------------------------------------------------
    // Parameters
    // ---------------------------------------------------------------

    private static List<WeatherParameter> BuildParameters(
        NetCdfFile file,
        List<Candidate> candidates,
        IReadOnlyList<DateTime> times,
        WeatherNativeGrid native,
        List<NetCdfIgnoredVariable> ignored)
    {
        // Known parameters first, best standard name first, so the preferred
        // variable keeps the canonical id when two map to it.
        var mapped = new List<(Candidate Candidate, WeatherParameterDefinition? Definition, WeatherLevel Level, double Factor, int Preference)>();
        foreach (var candidate in candidates)
        {
            var variable = candidate.Variable;
            var level = FindLevel(file, variable);
            var standardName = variable.GetText("standard_name");
            var definition = standardName is null ? null : WeatherParameters.FindByCfStandardName(standardName);
            double factor = 1;
            if (definition is not null)
            {
                var resolvedLevel = level ?? definition.Level ?? WeatherLevel.Surface;
                if (definition.Level is { } required && resolvedLevel != required)
                {
                    definition = null; // the same quantity at another level is an unknown parameter
                }
                else if (CfUnits.FactorTo(variable.GetText("units"), definition.Unit) is { } f)
                {
                    factor = f;
                    level = resolvedLevel;
                }
                else
                {
                    ignored.Add(new(variable.Name,
                        $"has standard_name '{standardName}' but units '{variable.GetText("units")}', which can't be converted to {WeatherUnits.GetLabel(definition.Unit)}; read as an unknown parameter"));
                    definition = null;
                }
            }

            int preference = definition is null
                ? int.MaxValue
                : IndexOf(definition.CfStandardNames, standardName!.Trim());
            mapped.Add((candidate, definition, level ?? WeatherLevel.Surface, factor, preference));
        }

        var seen = new HashSet<(string, WeatherLevel)>();
        var parameters = new List<WeatherParameter>(mapped.Count);
        foreach (var entry in mapped.OrderBy(m => m.Preference).ThenBy(m => candidates.IndexOf(m.Candidate)))
        {
            var variable = entry.Candidate.Variable;
            var steps = BuildSteps(file, entry.Candidate, times, native, entry.Factor);
            if (entry.Definition is { } definition && seen.Add((definition.Id, entry.Level)))
            {
                parameters.Add(new WeatherParameter(definition, entry.Level, steps, sourceName: variable.Name));
                continue;
            }

            string id = $"netcdf:{variable.Name}";
            if (!seen.Add((id, entry.Level)))
                continue;
            parameters.Add(new WeatherParameter(id, variable.GetText("units") ?? string.Empty, entry.Level, steps, sourceName: variable.Name));
        }

        // Keep the file's order for display.
        return parameters
            .OrderBy(p => candidates.FindIndex(c => c.Variable.Name == p.SourceName))
            .ToList();
    }

    private static WeatherTimeStep[] BuildSteps(
        NetCdfFile file,
        Candidate candidate,
        IReadOnlyList<DateTime> times,
        WeatherNativeGrid native,
        double unitFactor)
    {
        var variable = candidate.Variable;
        var packing = Packing.For(variable, unitFactor);
        if (candidate.TimeIndex < 0)
            return [new WeatherTimeStep(times[0], ct => Decode(file, candidate, -1, native, packing, ct))];

        int count = (int)variable.Dimensions[candidate.TimeIndex].Length;
        var steps = new WeatherTimeStep[Math.Min(count, times.Count)];
        for (int t = 0; t < steps.Length; t++)
        {
            int index = t;
            steps[t] = new WeatherTimeStep(times[t], ct => Decode(file, candidate, index, native, packing, ct));
        }

        return steps;
    }

    // Reads one time slice in the file's row-major latitude × longitude order,
    // with missing values as NaN and packing and unit conversion applied.
    private static float[] Decode(NetCdfFile file, Candidate candidate, int timeStep, WeatherNativeGrid native, Packing packing, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var variable = candidate.Variable;
        int rank = variable.Dimensions.Count;
        var start = new long[rank];
        var count = new long[rank];
        for (int d = 0; d < rank; d++)
            count[d] = d == candidate.TimeIndex ? 1 : variable.Dimensions[d].Length;
        if (candidate.TimeIndex >= 0)
            start[candidate.TimeIndex] = timeStep;

        var raw = file.Read(variable, start, count);
        var values = new float[raw.Length];
        int rows = native.Rows, columns = native.Columns;
        for (int i = 0; i < raw.Length; i++)
        {
            // Lon-major files are transposed into latitude rows.
            int destination = candidate.LongitudeFirst ? (i % rows) * columns + i / rows : i;
            values[destination] = packing.Unpack(raw[i]);
        }

        return values;
    }

    // ---------------------------------------------------------------
    // Coordinates
    // ---------------------------------------------------------------

    private enum Axis
    {
        Latitude,
        Longitude,
    }

    private static NetCdfVariable? FindAxis(NetCdfFile file, Axis axis)
    {
        string standardName = axis == Axis.Latitude ? "latitude" : "longitude";
        string axisLetter = axis == Axis.Latitude ? "Y" : "X";
        string[] units = axis == Axis.Latitude ? LatitudeUnits : LongitudeUnits;

        NetCdfVariable? Best(Func<NetCdfVariable, bool> matches) =>
            file.Variables.Where(v => v.IsNumeric && matches(v))
                .OrderByDescending(v => v.IsCoordinateVariable)
                .ThenBy(v => v.Dimensions.Count)
                .FirstOrDefault();

        return Best(v => string.Equals(v.GetText("standard_name"), standardName, StringComparison.Ordinal))
            ?? Best(v => units.Contains(v.GetText("units")?.ToLowerInvariant()))
            ?? Best(v => string.Equals(v.GetText("axis"), axisLetter, StringComparison.OrdinalIgnoreCase) && !IsProjected(v));
    }

    private static bool IsProjected(NetCdfVariable variable) =>
        variable.GetText("standard_name") is "projection_x_coordinate" or "projection_y_coordinate" or "grid_latitude" or "grid_longitude";

    private static void RequireOneDimensional(NetCdfVariable latitude, NetCdfVariable longitude)
    {
        if (latitude.Dimensions.Count >= 2 || longitude.Dimensions.Count >= 2)
        {
            throw new UnsupportedWeatherGridException(
                $"The grid is curvilinear: latitude and longitude are 2-D variables ({latitude}, {longitude}). Only regular latitude/longitude grids, with 1-D latitude and longitude coordinates, are supported.");
        }

        if (latitude.Dimensions.Count != 1 || longitude.Dimensions.Count != 1)
        {
            throw new CfConventionsException(
                $"The latitude and longitude coordinates ({latitude}, {longitude}) must each be 1-D variables along their own dimension.");
        }
    }

    private static void RequireLatitudeLongitudeMapping(NetCdfFile file, NetCdfVariable variable)
    {
        if (variable.GetText("grid_mapping") is not { Length: > 0 } mappingName)
            return;

        // CF §5.6: the attribute names a grid mapping variable (optionally
        // "name: coords" pairs in CF 1.7+); take the first variable named.
        var first = mappingName.Split([' ', ':'], StringSplitOptions.RemoveEmptyEntries)[0];
        var mapping = file.FindVariable(first);
        var gridType = mapping?.GetText("grid_mapping_name");
        if (string.Equals(gridType, "latitude_longitude", StringComparison.OrdinalIgnoreCase))
            return;

        throw new UnsupportedWeatherGridException(
            $"Variable '{variable.Name}' is on a projected grid (grid_mapping '{first}', grid_mapping_name '{gridType ?? "unknown"}'). Only regular latitude/longitude grids are supported; projected grids such as Lambert conformal or polar stereographic need reprojection, which isn't supported yet.");
    }

    private static NetCdfVariable? FindTime(NetCdfFile file)
    {
        static bool IsReferenceOrPeriod(NetCdfVariable v) =>
            v.GetText("standard_name") is "forecast_reference_time" or "forecast_period";

        static bool HasTimeUnits(NetCdfVariable v) =>
            v.GetText("units") is { } units && units.Contains(" since ", StringComparison.OrdinalIgnoreCase);

        NetCdfVariable? Best(Func<NetCdfVariable, bool> matches) =>
            file.Variables.Where(v => v.IsNumeric && v.Dimensions.Count <= 1 && !IsReferenceOrPeriod(v) && matches(v))
                .OrderByDescending(v => v.IsCoordinateVariable)
                .FirstOrDefault();

        return Best(v => string.Equals(v.GetText("standard_name"), "time", StringComparison.Ordinal))
            ?? Best(v => string.Equals(v.GetText("axis"), "T", StringComparison.OrdinalIgnoreCase))
            ?? Best(v => v.IsCoordinateVariable && HasTimeUnits(v));
    }

    private static IReadOnlyList<DateTime> ReadTimes(NetCdfFile file, NetCdfVariable time)
    {
        var units = time.GetText("units")
            ?? throw new CfConventionsException($"The time coordinate '{time.Name}' has no units, so its values can't be read as times.");
        var parsed = CfTimeUnits.Parse(units, time.GetText("calendar"));
        var values = UnpackCoordinate(time, file.ReadAll(time));
        var result = new DateTime[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = parsed.ToUtc(values[i]);
        return result;
    }

    private static DateTime? FindReferenceTime(NetCdfFile file)
    {
        foreach (var variable in file.Variables)
        {
            if (!string.Equals(variable.GetText("standard_name"), "forecast_reference_time", StringComparison.Ordinal) ||
                !variable.IsNumeric || variable.Dimensions.Count > 1 || variable.GetText("units") is not { } units)
            {
                continue;
            }

            var values = file.ReadAll(variable);
            if (values.Length > 0 && double.IsFinite(values[0]))
                return CfTimeUnits.Parse(units, variable.GetText("calendar")).ToUtc(UnpackCoordinate(variable, values)[0]);
        }

        foreach (var name in (string[])["forecast_reference_time", "reference_time", "run_time", "model_run_time"])
        {
            if (CfTimeUnits.ParseTime(file.GetGlobalText(name)) is { } time)
                return time;
        }

        return null;
    }

    private static double[] ReadCoordinate(NetCdfFile file, NetCdfVariable coordinate)
    {
        var values = UnpackCoordinate(coordinate, file.ReadAll(coordinate));
        if (values.Length < 2)
        {
            throw new UnsupportedWeatherGridException(
                string.Create(CultureInfo.InvariantCulture,
                    $"The '{coordinate.Name}' coordinate has {values.Length} point(s); a weather grid needs at least two along latitude and longitude to know its spacing."));
        }

        return values;
    }

    private static double[] UnpackCoordinate(NetCdfVariable coordinate, double[] raw)
    {
        double scale = coordinate.FindAttribute("scale_factor")?.FirstNumber ?? 1;
        double offset = coordinate.FindAttribute("add_offset")?.FirstNumber ?? 0;
        if (scale == 1 && offset == 0)
            return raw;
        return raw.Select(v => v * scale + offset).ToArray();
    }

    private static double RegularIncrement(NetCdfVariable coordinate, double[] values)
    {
        double increment = (values[^1] - values[0]) / (values.Length - 1);
        double tolerance = Math.Abs(increment) * SpacingTolerance + 1e-9;
        for (int i = 1; i < values.Length; i++)
        {
            double step = values[i] - values[i - 1];
            if (!double.IsFinite(step) || Math.Abs(step - increment) > tolerance)
            {
                throw new UnsupportedWeatherGridException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"The '{coordinate.Name}' coordinate isn't evenly spaced (step {step:0.######}° between points {i - 1} and {i}, against {increment:0.######}° on average), so the grid isn't a regular latitude/longitude grid (it may be a Gaussian grid). Only regular grids are supported."));
            }
        }

        return increment;
    }

    // A single height or depth from a length-1 vertical dimension or a scalar
    // coordinate named in "coordinates"; null when the variable has none.
    private static WeatherLevel? FindLevel(NetCdfFile file, NetCdfVariable variable)
    {
        var verticals = new List<NetCdfVariable>();
        foreach (var dimension in variable.Dimensions)
        {
            if (dimension.Length == 1 && file.FindVariable(dimension.Name) is { } coordinate && IsVertical(coordinate))
                verticals.Add(coordinate);
        }

        if (variable.GetText("coordinates") is { } coordinates)
        {
            foreach (var name in coordinates.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (file.FindVariable(name) is { Dimensions.Count: 0 } scalar && IsVertical(scalar))
                    verticals.Add(scalar);
            }
        }

        foreach (var vertical in verticals)
        {
            var values = file.ReadAll(vertical);
            if (values.Length == 0 || !double.IsFinite(values[0]))
                continue;

            double value = UnpackCoordinate(vertical, values)[0];
            double factor = CfUnits.FactorTo(vertical.GetText("units"), WeatherUnit.Metres) ?? 1;
            double metres = value * factor;
            bool down = string.Equals(vertical.GetText("positive"), "down", StringComparison.OrdinalIgnoreCase)
                || vertical.GetText("standard_name") == "depth";
            if (Math.Abs(metres) < 1e-6)
                return WeatherLevel.Surface;
            if (!down)
                return WeatherLevel.AboveGround(Length.FromMetres(metres));
            return WeatherLevel.Other(string.Create(CultureInfo.InvariantCulture, $"{metres:0.###} m below surface"));
        }

        return null;
    }

    private static bool IsVertical(NetCdfVariable coordinate) =>
        string.Equals(coordinate.GetText("axis"), "Z", StringComparison.OrdinalIgnoreCase)
        || coordinate.GetText("standard_name") is "height" or "depth" or "altitude"
        || coordinate.GetText("positive") is not null;

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static int IndexOf(NetCdfVariable variable, NetCdfDimension dimension)
    {
        for (int i = 0; i < variable.Dimensions.Count; i++)
        {
            if (string.Equals(variable.Dimensions[i].Name, dimension.Name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
                return i;
        }

        return int.MaxValue;
    }

    private static string ListVariables(NetCdfFile file)
    {
        if (file.Variables.Count == 0)
            return "none";

        return string.Join(", ", file.Variables.Select(v =>
            v.GetText("standard_name") is { } standardName ? $"{v} [{standardName}]" : v.ToString()));
    }

    private sealed record Candidate(NetCdfVariable Variable, int TimeIndex, bool LongitudeFirst);

    /// <summary>A variable's missing-value rules and unpacking.</summary>
    private sealed class Packing
    {
        private readonly double[] _missing;
        private readonly double _validMin;
        private readonly double _validMax;
        private readonly double _scale;
        private readonly double _offset;

        private Packing(double[] missing, double validMin, double validMax, double scale, double offset)
        {
            _missing = missing;
            _validMin = validMin;
            _validMax = validMax;
            _scale = scale;
            _offset = offset;
        }

        public static Packing For(NetCdfVariable variable, double unitFactor)
        {
            var missing = new List<double>();
            if (variable.FindAttribute("_FillValue") is { Numbers.Count: > 0 } fill)
                missing.AddRange(fill.Numbers);
            else if (DefaultFill(variable.DataType) is { } defaultFill)
                missing.Add(defaultFill);
            if (variable.FindAttribute("missing_value") is { Numbers.Count: > 0 } missingValue)
                missing.AddRange(missingValue.Numbers);

            double validMin = double.NegativeInfinity, validMax = double.PositiveInfinity;
            if (variable.FindAttribute("valid_range") is { Numbers.Count: >= 2 } range)
            {
                validMin = range.Numbers[0];
                validMax = range.Numbers[1];
            }

            if (variable.FindAttribute("valid_min")?.FirstNumber is { } min)
                validMin = min;
            if (variable.FindAttribute("valid_max")?.FirstNumber is { } max)
                validMax = max;

            double scale = variable.FindAttribute("scale_factor")?.FirstNumber ?? 1;
            double offset = variable.FindAttribute("add_offset")?.FirstNumber ?? 0;
            return new Packing(missing.ToArray(), validMin, validMax, scale * unitFactor, offset * unitFactor);
        }

        public float Unpack(double raw)
        {
            if (double.IsNaN(raw) || raw < _validMin || raw > _validMax)
                return float.NaN;
            foreach (var missing in _missing)
            {
                // A float32 fill stored as float and read as double compares exactly;
                // compare as float too for fills written in double attributes.
                if (raw == missing || (float)raw == (float)missing)
                    return float.NaN;
            }

            return (float)(raw * _scale + _offset);
        }

        // The NetCDF default fill values (netcdf.h NC_FILL_*). Bytes have no
        // default fill for missing-value purposes (NetCDF User Guide).
        private static double? DefaultFill(NetCdfDataType type) => type switch
        {
            NetCdfDataType.Short => -32767,
            NetCdfDataType.Int => -2147483647,
            NetCdfDataType.Float => 9.9692099683868690e+36f,
            NetCdfDataType.Double => 9.9692099683868690e+36,
            NetCdfDataType.UShort => 65535,
            NetCdfDataType.UInt => 4294967295,
            _ => null,
        };
    }
}
