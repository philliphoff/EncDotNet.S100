namespace EncDotNet.S100.Datasets.S111.Writing;

// The S-100 / S-111 Edition 2.0.0 enumerations the writer stores as HDF5
// enumerations (unsigned 8-bit). The numbers are the contract; the member
// names become the HDF5 labels.

/// <summary>S-100 Part 10c <c>dataCodingFormat</c>.</summary>
internal enum S111DataCodingFormat : byte
{
    StationwiseFixed = 8,
}

/// <summary>S-111 <c>typeOfCurrentData</c>.</summary>
internal enum S111TypeOfCurrentData : byte
{
    History = 1,
    RealTime = 2,
    AstronomicalPrediction = 3,
    AnalysisOrHybrid = 4,
    HydrodynamicHindcast = 5,
    HydrodynamicForecast = 6,
}

/// <summary>S-111 <c>depthTypeIndex</c>.</summary>
internal enum S111DepthTypeIndex : byte
{
    LayerAverage = 1,
    HeightOrDepth = 2,
}

/// <summary>S-100 Part 10c <c>verticalCoordinateBase</c>.</summary>
internal enum S111VerticalCoordinateBase : byte
{
    SeaSurface = 1,
}

/// <summary>S-100 Part 10c <c>commonPointRule</c>.</summary>
internal enum S111CommonPointRule : byte
{
    All = 4,
}

/// <summary>S-100 Part 10c <c>interpolationType</c>.</summary>
internal enum S111InterpolationType : byte
{
    Discrete = 10,
}
