namespace EncDotNet.S100.PerfRunner;

internal readonly record struct GeographicBounds(
    double South,
    double West,
    double North,
    double East);

internal readonly record struct ViewportStressStep(
    int Index,
    double Latitude,
    double Longitude,
    double Zoom);

internal static class ViewerStressRoute
{
    public static IReadOnlyList<ViewportStressStep> Create(
        GeographicBounds bounds,
        double minimumZoom,
        double maximumZoom,
        int stepCount)
    {
        Validate(bounds, minimumZoom, maximumZoom, stepCount);

        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(stepCount)));
        var rows = Math.Max(1, (int)Math.Ceiling((double)stepCount / columns));
        var steps = new List<ViewportStressStep>(stepCount);

        for (var index = 0; index < stepCount; index++)
        {
            var row = index / columns;
            var columnInRow = index % columns;
            var column = row % 2 == 0
                ? columnInRow
                : columns - 1 - columnInRow;

            var latitudeFraction = rows == 1 ? 0.5 : (double)row / (rows - 1);
            var longitudeFraction = columns == 1 ? 0.5 : (double)column / (columns - 1);
            var latitude = Lerp(bounds.South, bounds.North, latitudeFraction);
            var longitude = Lerp(bounds.West, bounds.East, longitudeFraction);
            var zoom = TriangleWave(minimumZoom, maximumZoom, index, stepCount);
            steps.Add(new ViewportStressStep(index, latitude, longitude, zoom));
        }

        return steps;
    }

    public static IReadOnlyList<ViewportStressStep> CreateNavigation(
        GeographicBounds bounds,
        double minimumZoom,
        double maximumZoom,
        int stepCount)
    {
        Validate(bounds, minimumZoom, maximumZoom, stepCount);

        var lowLatitude = Lerp(bounds.South, bounds.North, 0.3);
        var highLatitude = Lerp(bounds.South, bounds.North, 0.7);
        var westLongitude = Lerp(bounds.West, bounds.East, 0.1);
        var eastLongitude = Lerp(bounds.West, bounds.East, 0.9);
        var middleZoom = Lerp(minimumZoom, maximumZoom, 0.5);
        ViewportStressStep[] keyFrames =
        [
            new(0, lowLatitude, westLongitude, minimumZoom),
            new(0, lowLatitude, eastLongitude, minimumZoom),
            new(0, lowLatitude, eastLongitude, maximumZoom),
            new(0, highLatitude, eastLongitude, maximumZoom),
            new(0, highLatitude, westLongitude, maximumZoom),
            new(0, highLatitude, westLongitude, minimumZoom),
            new(0, Lerp(bounds.South, bounds.North, 0.5),
                Lerp(bounds.West, bounds.East, 0.5), middleZoom),
        ];

        if (stepCount == 1)
        {
            return [keyFrames[0]];
        }

        var steps = new List<ViewportStressStep>(stepCount);
        var segmentCount = keyFrames.Length - 1;
        for (var index = 0; index < stepCount; index++)
        {
            var routeProgress = (double)index / (stepCount - 1) * segmentCount;
            var segment = Math.Min((int)routeProgress, segmentCount - 1);
            var fraction = index == stepCount - 1 ? 1 : routeProgress - segment;
            var start = keyFrames[segment];
            var end = keyFrames[segment + 1];
            steps.Add(new ViewportStressStep(
                index,
                Lerp(start.Latitude, end.Latitude, fraction),
                Lerp(start.Longitude, end.Longitude, fraction),
                Lerp(start.Zoom, end.Zoom, fraction)));
        }

        return steps;
    }

    private static void Validate(
        GeographicBounds bounds,
        double minimumZoom,
        double maximumZoom,
        int stepCount)
    {
        if (bounds.South >= bounds.North)
        {
            throw new ArgumentException("South must be less than north.", nameof(bounds));
        }
        if (bounds.West >= bounds.East)
        {
            throw new ArgumentException("West must be less than east.", nameof(bounds));
        }
        if (minimumZoom > maximumZoom)
        {
            throw new ArgumentException(
                "Minimum zoom must be less than or equal to maximum zoom.",
                nameof(minimumZoom));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(stepCount, 1);
    }

    private static double TriangleWave(
        double minimum,
        double maximum,
        int index,
        int count)
    {
        if (minimum == maximum || count == 1)
        {
            return minimum;
        }

        var phase = (double)index / (count - 1);
        var fraction = phase <= 0.5 ? phase * 2 : (1 - phase) * 2;
        return Lerp(minimum, maximum, fraction);
    }

    private static double Lerp(double start, double end, double fraction) =>
        start + (end - start) * fraction;
}
