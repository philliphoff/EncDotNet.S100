namespace EncDotNet.S100.Datasets.S104.Writing;

// The S-100 / S-104 Edition 2.0.0 enumerations the writer stores as HDF5
// enumerations (unsigned 8-bit). The numbers are the contract; the member
// names become the HDF5 labels, PascalCase as NOAA's own S-104 files
// already label waterLevelTrend.

/// <summary>S-100 Part 10c <c>dataCodingFormat</c>.</summary>
internal enum S104DataCodingFormat : byte
{
    StationwiseFixed = 8,
}

/// <summary>S-104 Edition 2.0.0 <c>dataDynamicity</c>.</summary>
internal enum S104DataDynamicity : byte
{
    Observation = 1,
    AstronomicalPrediction = 2,
    AnalysisOrHybrid = 3,
    HydrodynamicHindcast = 4,
    HydrodynamicForecast = 5,
}

/// <summary>S-104 Edition 2.0.0 <c>waterLevelTrend</c>; 0 is the fill value.</summary>
internal enum S104WaterLevelTrend : byte
{
    Decreasing = 1,
    Increasing = 2,
    Steady = 3,
}

/// <summary>S-100 Part 10c <c>commonPointRule</c>.</summary>
internal enum S104CommonPointRule : byte
{
    All = 4,
}

/// <summary>S-100 Part 10c <c>interpolationType</c>.</summary>
internal enum S104InterpolationType : byte
{
    Discrete = 10,
}

/// <summary>S-100 Part 10c <c>verticalCoordinateBase</c>.</summary>
internal enum S104VerticalCoordinateBase : byte
{
    VerticalDatum = 2,
}

/// <summary>S-100 Part 10c <c>verticalDatumReference</c>.</summary>
internal enum S104VerticalDatumReference : byte
{
    S100VerticalDatum = 1,
}
