using System.Globalization;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The vertical level of a weather parameter, for example 10 m above the
/// ground for wind, or mean sea level for pressure.
/// </summary>
public sealed record WeatherLevel
{
    private WeatherLevel(WeatherLevelKind kind, Length? height, string description)
    {
        Kind = kind;
        Height = height;
        Description = description;
    }

    /// <summary>The kind of level.</summary>
    public WeatherLevelKind Kind { get; }

    /// <summary>The height for <see cref="WeatherLevelKind.HeightAboveGround"/>; otherwise <see langword="null"/>.</summary>
    public Length? Height { get; }

    /// <summary>A short description for display, for example <c>"10 m above ground"</c>.</summary>
    public string Description { get; }

    /// <summary>The ground or water surface.</summary>
    public static WeatherLevel Surface { get; } = new(WeatherLevelKind.Surface, null, "surface");

    /// <summary>Mean sea level.</summary>
    public static WeatherLevel MeanSeaLevel { get; } = new(WeatherLevelKind.MeanSeaLevel, null, "mean sea level");

    /// <summary>A height above the ground or sea surface.</summary>
    /// <param name="height">The height.</param>
    public static WeatherLevel AboveGround(Length height) =>
        new(
            WeatherLevelKind.HeightAboveGround,
            height,
            string.Create(CultureInfo.InvariantCulture, $"{height.TotalMetres:0.###} m above ground"));

    /// <summary>A level the model doesn't classify, described by the reader.</summary>
    /// <param name="description">The reader's description of the level.</param>
    /// <exception cref="ArgumentException"><paramref name="description"/> is empty.</exception>
    public static WeatherLevel Other(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new(WeatherLevelKind.Other, null, description);
    }

    /// <summary>Returns <see cref="Description"/>.</summary>
    public override string ToString() => Description;
}
