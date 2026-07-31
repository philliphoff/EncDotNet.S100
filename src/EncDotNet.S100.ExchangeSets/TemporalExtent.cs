
namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Represents the temporal extent of a dataset, including the beginning and end time instants.
/// </summary>
public sealed class TemporalExtent
{
    public DateTime? TimeInstantBegin { get; init; }

    public DateTime? TimeInstantEnd { get; init; }
}
