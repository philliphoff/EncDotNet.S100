using System.Globalization;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// One field of a GRIB2 file, as indexed when the file opens: the parameter,
/// level, times, grid and packing of one data section, and where it is in
/// the file. The values are decoded only when
/// <see cref="GribFile.ReadValues"/> is called.
/// </summary>
/// <remarks>
/// <para>
/// A message usually holds one field. A message that repeats sections 2 to 7
/// (or 3 to 7, or 4 to 7) holds one field per Section 7; each field takes the
/// most recent Section 1 to 6 before it. NOAA and ECMWF files hold one field
/// per message.
/// </para>
/// <para>
/// A field with an unsupported grid, product or data template is still
/// listed, with <see cref="UnsupportedReason"/> naming the template, so a
/// caller can show what the file holds; reading its values throws.
/// </para>
/// </remarks>
public sealed class GribField
{
    internal GribField(
        int index,
        int messageIndex,
        int fieldNumber,
        long messageOffset,
        long messageLength,
        byte discipline,
        GribIdentification identification,
        GribGrid grid,
        GribProduct product,
        GribDataRepresentation dataRepresentation,
        bool hasBitmap,
        string? unsupportedReason,
        long bitmapOffset,
        long bitmapLength,
        long dataOffset,
        long dataLength)
    {
        Index = index;
        MessageIndex = messageIndex;
        FieldNumber = fieldNumber;
        MessageOffset = messageOffset;
        MessageLength = messageLength;
        Discipline = discipline;
        Identification = identification;
        Grid = grid;
        Product = product;
        DataRepresentation = dataRepresentation;
        HasBitmap = hasBitmap;
        UnsupportedReason = unsupportedReason;
        BitmapOffset = bitmapOffset;
        BitmapLength = bitmapLength;
        DataOffset = dataOffset;
        DataLength = dataLength;
    }

    /// <summary>The field's position in <see cref="GribFile.Fields"/>.</summary>
    public int Index { get; }

    /// <summary>The zero-based number of the message that holds the field.</summary>
    public int MessageIndex { get; }

    /// <summary>The zero-based number of the field within its message (non-zero only for repeated sections).</summary>
    public int FieldNumber { get; }

    /// <summary>The byte offset of the message ("GRIB") in the file.</summary>
    public long MessageOffset { get; }

    /// <summary>The length of the message in bytes, from Section 0.</summary>
    public long MessageLength { get; }

    /// <summary>The discipline (Section 0, code table 0.0), for example <c>0</c> meteorological or <c>10</c> oceanographic.</summary>
    public byte Discipline { get; }

    /// <summary>The identification section: centre, tables and reference time.</summary>
    public GribIdentification Identification { get; }

    /// <summary>The grid definition.</summary>
    public GribGrid Grid { get; }

    /// <summary>The product definition: parameter, level and times.</summary>
    public GribProduct Product { get; }

    /// <summary>The data representation: how the values are packed.</summary>
    public GribDataRepresentation DataRepresentation { get; }

    /// <summary>Whether a bitmap (Section 6) marks some grid points as missing.</summary>
    public bool HasBitmap { get; }

    /// <summary>
    /// Why the field's values can't be decoded, naming the unsupported
    /// template; or <see langword="null"/> when they can.
    /// </summary>
    public string? UnsupportedReason { get; }

    /// <summary>Whether the field's values can be decoded (<see cref="UnsupportedReason"/> is <see langword="null"/>).</summary>
    public bool IsSupported => UnsupportedReason is null;

    /// <summary>The parameter as (discipline, category, number).</summary>
    public GribParameterCode Parameter => new(Discipline, Product.ParameterCategory, Product.ParameterNumber);

    /// <summary>The reference time (Section 1), in UTC: for a forecast, the model run time.</summary>
    public DateTime ReferenceTime => Identification.ReferenceTime;

    /// <summary>
    /// The time the values are valid for, in UTC: the reference time plus the
    /// forecast time, or for a statistic over a time interval (template 4.8)
    /// the end of the interval. <see langword="null"/> when the product
    /// template isn't supported.
    /// </summary>
    public DateTime? ValidTime => Product.GetValidTime(ReferenceTime);

    internal long BitmapOffset { get; }

    internal long BitmapLength { get; }

    internal long DataOffset { get; }

    internal long DataLength { get; }

    /// <summary>
    /// The grid as the weather model's <see cref="WeatherNativeGrid"/>, with
    /// increments signed by the scanning mode.
    /// </summary>
    /// <exception cref="UnsupportedWeatherGridException">
    /// The grid isn't a regular latitude/longitude grid (template 3.0); the
    /// message names the template.
    /// </exception>
    public WeatherNativeGrid ToWeatherNativeGrid()
    {
        if (Grid.LatLon is not { } latLon)
            throw new UnsupportedWeatherGridException(UnsupportedReason ?? "The grid isn't a regular latitude/longitude grid.");
        return latLon.ToWeatherNativeGrid();
    }

    /// <summary>
    /// The field's level as a <see cref="WeatherLevel"/>: the surface (fixed
    /// surface type 1), mean sea level (101), a height above ground (103), or
    /// <see cref="WeatherLevel.Other"/> describing anything else, such as a
    /// layer or NCEP's "1 in sequence" swell partitions (type 241).
    /// </summary>
    public WeatherLevel ToWeatherLevel()
    {
        var first = Product.FirstSurface;
        var second = Product.SecondSurface;
        if (first is not { } surface)
            return WeatherLevel.Other("no fixed surface");
        if (second is { } top)
            return WeatherLevel.Other(string.Create(CultureInfo.InvariantCulture, $"layer from GRIB2 surface {surface} to {top}"));

        return surface.Type switch
        {
            GribFixedSurface.GroundOrWater => WeatherLevel.Surface,
            GribFixedSurface.MeanSeaLevel => WeatherLevel.MeanSeaLevel,
            GribFixedSurface.HeightAboveGround when surface.Value is { } metres => WeatherLevel.AboveGround(Length.FromMetres(metres)),
            GribFixedSurface.Isobaric when surface.Value is { } pascals =>
                WeatherLevel.Other(string.Create(CultureInfo.InvariantCulture, $"{pascals / 100:0.###} hPa")),
            _ => WeatherLevel.Other(string.Create(CultureInfo.InvariantCulture, $"GRIB2 fixed surface {surface}")),
        };
    }

    /// <summary>
    /// Finds the canonical weather parameter (design note weather-sources.md
    /// §3.5) this field holds: its parameter code through
    /// <see cref="WeatherParameters.FindByGribCode"/>, and its level matching
    /// the row's level where the row has one (10 m for the wind rows, mean sea
    /// level for pressure).
    /// </summary>
    /// <returns>The row, or <see langword="null"/> for a parameter or level the table doesn't have.</returns>
    public WeatherParameterDefinition? FindWeatherParameter()
    {
        var definition = WeatherParameters.FindByGribCode(Parameter);
        if (definition is null)
            return null;
        if (definition.Level is { } level && !level.Equals(ToWeatherLevel()))
            return null;
        return definition;
    }

    /// <summary>
    /// Returns a one-line inventory entry, for example
    /// <c>"#3 0,2,2 type 103 = 10 ref 2026-10-25T00:00Z valid 2026-10-25T03:00Z 3.0 4.0 5.3"</c>.
    /// </summary>
    public override string ToString()
    {
        var valid = ValidTime is { } time ? time.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture) : "?";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{Index} {Parameter} {Product.FirstSurface?.ToString() ?? "no surface"} ref {ReferenceTime:yyyy-MM-dd'T'HH:mm'Z'} valid {valid} 3.{Grid.TemplateNumber} 4.{Product.TemplateNumber} 5.{DataRepresentation.TemplateNumber}");
    }
}
