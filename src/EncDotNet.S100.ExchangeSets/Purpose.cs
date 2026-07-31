
namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Enum for Purpose, representing the intended purpose of a dataset.
/// </summary>
public enum Purpose
{
    NewDataset = 1,
    NewEdition = 2,
    Update = 3,
    Reissue = 4,
    Cancellation = 5,
    Delta = 6,
}
