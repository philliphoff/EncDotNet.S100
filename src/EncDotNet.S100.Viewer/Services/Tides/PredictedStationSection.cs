using System.Text.Json.Serialization;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// A predicted tide or current station in a pick (data source design note
/// slice 7d): SoundCharts presents it as the station's card (tide predictions
/// design D8), with its predicted curve following the view time; another host
/// shows its rows.
/// </summary>
/// <param name="SectionKind">The section's kind: <see cref="TideKind"/> or <see cref="CurrentKind"/>.</param>
/// <param name="StationId">The station id, such as <c>9447130</c> or <c>PUG1701_33</c>.</param>
/// <param name="Name">The station's name.</param>
internal sealed record PredictedStationSection(string SectionKind, string StationId, string Name) : PickSection
{
    /// <summary>The kind of a predicted tide station.</summary>
    public const string TideKind = "tides.station.predicted";

    /// <summary>The kind of a predicted current station.</summary>
    public const string CurrentKind = "tides.current.predicted";

    /// <inheritdoc />
    [JsonIgnore]
    public override string Kind => SectionKind;

    /// <inheritdoc />
    public override IReadOnlyList<PickAttribute> ToRows() =>
        [new PickAttribute { Code = "stationId", Name = "Station", RawValue = StationId }];
}
